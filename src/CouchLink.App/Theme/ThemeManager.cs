using System.Windows;
using CouchLink.App.Presentation;

namespace CouchLink.App.Theme;

/// <summary>
/// Puts the theme into the app's resources: the palette (dark, or High Contrast system colors, and
/// swapped when Windows switches), the motion values (Theme/Motion.cs) and Controls.xaml (styles, which
/// merges Icons.xaml). The motion values go in before Controls.xaml because its storyboards read them with
/// StaticResource; when Reduce motion changes, both are swapped, and controls whose style is a
/// DynamicResource (or implicit) pick up the new templates at once.
/// </summary>
internal static class ThemeManager
{
    private const int PaletteIndex = 0, MotionIndex = 1, ControlsIndex = 2;
    private static readonly Uri TokensUri = new("pack://application:,,,/CouchLink.App;component/Theme/Tokens.xaml");
    private static readonly Uri HighContrastUri = new("pack://application:,,,/CouchLink.App;component/Theme/HighContrast.xaml");
    private static readonly Uri ControlsUri = new("pack://application:,,,/CouchLink.App;component/Theme/Controls.xaml");
    private static Application? _app;

    public static void Install(Application app)
    {
        if (_app is not null)
            return;
        _app = app;
        Fill(app.Resources);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                app.Dispatcher.InvokeAsync(() => app.Resources.MergedDictionaries[PaletteIndex] = Palette());
        };
    }

    /// <summary>Turns Reduce motion on or off for the whole app, live.</summary>
    public static void SetReducedMotion(bool reduced)
    {
        if (Motion.Reduced == reduced)
            return;
        Motion.Reduced = reduced;
        if (_app is null)
            return;
        var merged = _app.Resources.MergedDictionaries;
        merged[MotionIndex] = Motion.Resources(reduced);
        merged[ControlsIndex] = new ResourceDictionary { Source = ControlsUri }; // its storyboards read the new values
    }

    /// <summary>Applies <paramref name="settings"/> now and whenever it changes, until disposed.</summary>
    public static IDisposable Follow(MotionSettings settings)
    {
        void Apply() => SetReducedMotion(settings.ReduceMotion);
        settings.Changed += Apply;
        Apply();
        return new Unfollow(() => settings.Changed -= Apply);
    }

    /// <summary>
    /// For a window on its own thread (the crash dialog): its own copy of the theme, so it never
    /// reaches into the app's resources across threads. False, and the stock look, if anything fails.
    /// </summary>
    public static bool TryInstallInto(FrameworkElement element)
    {
        try
        {
            Fill(element.Resources);
            return true;
        }
        catch
        {
            element.Resources.MergedDictionaries.Clear();
            return false;
        }
    }

    private static void Fill(ResourceDictionary resources)
    {
        resources.MergedDictionaries.Add(Palette());
        resources.MergedDictionaries.Add(Motion.Resources(Motion.Reduced));
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = ControlsUri });
    }

    private static ResourceDictionary Palette() =>
        new() { Source = SystemParameters.HighContrast ? HighContrastUri : TokensUri };

    private sealed class Unfollow(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
