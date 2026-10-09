using System.Windows;

namespace CouchLink.App.Theme;

/// <summary>
/// Puts the theme into the app's resources: the palette (dark, or High Contrast system colors, and
/// swapped when Windows switches) and Controls.xaml (styles, which merges Icons.xaml).
/// FastDuration goes in first because Controls.xaml's templates read it with StaticResource.
/// </summary>
internal static class ThemeManager
{
    private static readonly Uri TokensUri = new("pack://application:,,,/CouchLink.App;component/Theme/Tokens.xaml");
    private static readonly Uri HighContrastUri = new("pack://application:,,,/CouchLink.App;component/Theme/HighContrast.xaml");
    private static readonly Uri ControlsUri = new("pack://application:,,,/CouchLink.App;component/Theme/Controls.xaml");
    private static bool _installed;

    /// <summary>Windows "Show animations in Windows". Off: hovers and screen changes are instant.</summary>
    public static bool AnimationsOn => SystemParameters.ClientAreaAnimation;

    public static Duration FastDurationFor(bool animationsOn) =>
        new(animationsOn ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero);

    public static void Install(Application app)
    {
        if (_installed)
            return;
        _installed = true;
        Fill(app.Resources);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                app.Dispatcher.InvokeAsync(() => app.Resources.MergedDictionaries[0] = Palette());
        };
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
        resources["FastDuration"] = FastDurationFor(AnimationsOn);
        resources.MergedDictionaries.Add(Palette());
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = ControlsUri });
    }

    private static ResourceDictionary Palette() =>
        new() { Source = SystemParameters.HighContrast ? HighContrastUri : TokensUri };
}
