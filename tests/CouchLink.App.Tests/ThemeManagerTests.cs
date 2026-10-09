using System.Collections;
using System.Windows;
using System.Windows.Media;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

public class ThemeManagerTests
{
    private static ResourceDictionary Load(string file) =>
        Wpf.Run(() => new ResourceDictionary { Source = new Uri($"pack://application:,,,/CouchLink.App;component/Theme/{file}") });

    private static HashSet<string> Keys(ResourceDictionary dictionary) =>
        Wpf.Run(() => dictionary.Keys.OfType<string>().ToHashSet());

    [Fact]
    public void High_contrast_defines_every_dark_brush_and_nothing_else()
    {
        var dark = Keys(Load("Tokens.xaml"));
        var contrast = Keys(Load("HighContrast.xaml"));
        Assert.NotEmpty(dark);
        Assert.Equal(dark.Order(), contrast.Order());
    }

    [Theory]
    [InlineData("Tokens.xaml")]
    [InlineData("HighContrast.xaml")]
    public void Every_palette_entry_is_a_brush(string file)
    {
        var dictionary = Load(file);
        Wpf.Run(() =>
        {
            foreach (DictionaryEntry entry in dictionary)
                Assert.True(entry.Value is Brush, $"{file}: {entry.Key} is {entry.Value?.GetType().Name}");
        });
    }

    [Fact]
    public void Install_puts_the_palette_icons_and_durations_in_the_app()
    {
        Wpf.Run(() =>
        {
            var app = Application.Current;
            ThemeManager.Install(app);
            ThemeManager.Install(app); // a second call changes nothing
            Assert.Equal(2, app.Resources.MergedDictionaries.Count);
            Assert.IsType<Duration>(app.Resources["FastDuration"]);
            Assert.IsAssignableFrom<Geometry>(app.FindResource("IconMonitor"));
            if (!SystemParameters.HighContrast)
                Assert.Equal(Color.FromRgb(0x7C, 0x3A, 0xED), ((SolidColorBrush)app.FindResource("PrimaryBrush")).Color);
        });
    }

    [Fact]
    public void Durations_are_zero_when_windows_animations_are_off()
    {
        Assert.Equal(TimeSpan.Zero, ThemeManager.FastDurationFor(animationsOn: false).TimeSpan);
        Assert.Equal(TimeSpan.FromMilliseconds(120), ThemeManager.FastDurationFor(animationsOn: true).TimeSpan);
    }

    [Fact]
    public void A_window_on_another_thread_can_get_its_own_copy_of_the_theme()
    {
        Wpf.Run(() =>
        {
            var element = new FrameworkElement();
            Assert.True(ThemeManager.TryInstallInto(element));
            Assert.True(element.Resources.Contains("FastDuration"));
            Assert.NotNull(element.TryFindResource("CardBrush"));
        });
    }
}
