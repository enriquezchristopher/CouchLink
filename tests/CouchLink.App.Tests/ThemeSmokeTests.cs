using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

/// <summary>Lays out one of every themed control, which instantiates each template and resolves its resources.</summary>
public class ThemeSmokeTests
{
    private static T Laid<T>(T element) where T : FrameworkElement
    {
        var host = new Border { Child = element };
        host.Measure(new Size(400, 600));
        host.Arrange(new Rect(0, 0, 400, 600));
        return element;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("PrimaryButton")]
    [InlineData("DangerButton")]
    [InlineData("DangerFilledButton")]
    [InlineData("GhostButton")]
    [InlineData("HeaderButton")]
    [InlineData("HeroButton")]
    [InlineData("HeroPrimaryButton")]
    [InlineData("HostCardButton")]
    [InlineData("ControlRowButton")]
    public void Every_button_style_lays_out(string? style)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var button = new Button { Content = "Go" };
            if (style is not null)
                button.Style = (Style)Application.Current.FindResource(style);
            Laid(button);
            Assert.NotNull(button.Template);
            Assert.True(VisualTreeHelper.GetChildrenCount(button) > 0);
        });
    }

    [Fact]
    public void The_implicit_button_is_the_secondary_look()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var button = Laid(new Button { Content = "Go" });
            Assert.Same(Application.Current.FindResource("CardBrush"), button.Background);
            Assert.Same(Application.Current.FindResource("ButtonTemplate"), button.Template);
        });
    }

    [Fact]
    public void Inputs_lay_out()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var app = Application.Current;
            var panel = new StackPanel();
            panel.Children.Add(new CheckBox { Content = "Plain" });
            panel.Children.Add(new CheckBox { Content = "Switch", Style = (Style)app.FindResource("ToggleSwitch"), IsChecked = true });
            panel.Children.Add(new RadioButton { Content = "Balanced", Style = (Style)app.FindResource("SegmentedItem"), IsChecked = true });
            var combo = new ComboBox();
            combo.Items.Add(new ComboBoxItem { Content = "1080p" });
            combo.SelectedIndex = 0;
            panel.Children.Add(combo);
            panel.Children.Add(new TextBox());
            panel.Children.Add(new Slider { Minimum = 1, Maximum = 10, Value = 5 });
            panel.Children.Add(new Expander { Header = "Stats", Content = new TextBlock { Text = "x" }, IsExpanded = true });
            panel.Children.Add(new ScrollViewer { Height = 50, Content = new Border { Height = 500 }, VerticalScrollBarVisibility = ScrollBarVisibility.Visible });
            Laid(panel);
            foreach (Control control in panel.Children)
                Assert.True(VisualTreeHelper.GetChildrenCount(control) > 0, control.GetType().Name);
        });
    }

    [Fact]
    public void Text_and_card_styles_exist()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            foreach (var key in new[] { "DisplayText", "TitleText", "SubtitleText", "BodyMutedText", "CaptionText", "OverlineText", "MonoText" })
                Assert.Equal(typeof(TextBlock), ((Style)Application.Current.FindResource(key)).TargetType);
            Assert.Equal(typeof(Border), ((Style)Application.Current.FindResource("Card")).TargetType);
            Assert.Equal(typeof(Border), ((Style)Application.Current.FindResource("Segmented")).TargetType);
        });
    }

    [Fact]
    public void Touch_targets_are_at_least_32_and_hero_buttons_72()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var app = Application.Current;
            Assert.True(MinHeightOf((Style)app.FindResource("HeaderButton")) >= 32);
            Assert.True(MinHeightOf((Style)app.FindResource("SegmentedItem")) >= 32);
            Assert.True(MinHeightOf((Style)app.FindResource(typeof(CheckBox))) >= 32);
            Assert.True(MinHeightOf((Style)app.FindResource("ToggleSwitch")) >= 32);
            Assert.Equal(72, MinHeightOf((Style)app.FindResource("HeroButton")));
        });
    }

    private static double MinHeightOf(Style? style)
    {
        for (; style is not null; style = style.BasedOn)
            foreach (var setter in style.Setters.OfType<Setter>())
                if (setter.Property == FrameworkElement.MinHeightProperty)
                    return (double)setter.Value;
        return 0;
    }

    [Fact]
    public void A_text_box_shows_its_placeholder_only_while_empty()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var box = new TextBox();
            Ui.ThemeProps.SetPlaceholder(box, "192.168.1.23");
            Laid(box);
            var hint = (TextBlock)box.Template.FindName("Hint", box);
            Assert.Equal(Visibility.Visible, hint.Visibility);
            box.Text = "1";
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
        });
    }
}
