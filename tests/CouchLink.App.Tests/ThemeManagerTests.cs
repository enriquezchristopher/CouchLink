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
            Assert.Equal(3, app.Resources.MergedDictionaries.Count); // palette, motion, controls
            Assert.IsType<Duration>(app.FindResource("MotionHover"));
            Assert.IsAssignableFrom<Geometry>(app.FindResource("IconMonitor"));
            if (!SystemParameters.HighContrast)
                Assert.Equal(Color.FromRgb(0x7C, 0x3A, 0xED), ((SolidColorBrush)app.FindResource("PrimaryBrush")).Color);
        });
    }

    [Fact]
    public void Motion_is_on_by_default_whatever_windows_says()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            Assert.False(Motion.Reduced);
            Assert.Equal(TimeSpan.FromMilliseconds(150), ((Duration)Application.Current.FindResource("MotionHover")).TimeSpan);
        });
    }

    [Fact]
    public void Reduce_motion_makes_template_motion_instant_or_a_short_fade()
    {
        Wpf.Run(() =>
        {
            var on = Motion.Resources(reduced: false);
            var off = Motion.Resources(reduced: true);
            Assert.Equal(Motion.ResourceKeys.Order(), on.Keys.OfType<string>().Order());
            Assert.Equal(Motion.ResourceKeys.Order(), off.Keys.OfType<string>().Order());
            Assert.Equal(TimeSpan.FromMilliseconds(150), ((Duration)on["MotionHover"]).TimeSpan);
            Assert.Equal(TimeSpan.FromMilliseconds(90), ((Duration)on["MotionPressDown"]).TimeSpan);
            Assert.Equal(TimeSpan.FromMilliseconds(160), ((Duration)on["MotionPressUp"]).TimeSpan);
            Assert.Equal(TimeSpan.FromMilliseconds(180), ((Duration)on["MotionMove"]).TimeSpan);
            foreach (var key in off.Keys.OfType<string>())
                if (off[key] is Duration duration)
                    Assert.True(duration.TimeSpan <= TimeSpan.FromMilliseconds(120), key);
            Assert.Equal(TimeSpan.Zero, ((Duration)off["MotionMove"]).TimeSpan); // movement is instant
            Assert.Equal(1.0, (double)off["PressScale"]);
            Assert.Equal(0.0, (double)off["RevealRise"]);
            Assert.True(((Freezable)on["EaseOut"]).IsFrozen);
        });
    }

    [Fact]
    public void A_window_on_another_thread_can_get_its_own_copy_of_the_theme()
    {
        Wpf.Run(() =>
        {
            var element = new FrameworkElement();
            Assert.True(ThemeManager.TryInstallInto(element));
            Assert.NotNull(element.TryFindResource("MotionHover"));
            Assert.NotNull(element.TryFindResource("CardBrush"));
        });
    }
}
