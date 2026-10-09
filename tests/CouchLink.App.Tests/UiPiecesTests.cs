using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CouchLink.App.Theme;
using CouchLink.App.Ui;

namespace CouchLink.App.Tests;

public class UiPiecesTests
{
    private static object Res(string key) => Application.Current.FindResource(key);

    [Fact]
    public void Player_chip_shows_the_slot_in_its_color()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var chip = new PlayerChip { Slot = 3 };
            Assert.Equal("P3", chip.Text);
            Assert.Equal(PlayerColors.ColorFor(3), ((SolidColorBrush)chip.Background).Color);
            Assert.Equal(28, chip.Width);
            chip.Large = true;
            Assert.Equal(52, chip.Width);
        });
    }

    [Fact]
    public void Key_cap_without_a_key_says_no_key()
    {
        Wpf.Run(() =>
        {
            Assert.Equal("No key", new KeyCap().Text);
            Assert.Equal("Left Shift", new KeyCap { Key = "Left Shift" }.Text);
            Assert.Equal("No key", new KeyCap { Key = "" }.Text);
        });
    }

    [Theory]
    [InlineData(PillKind.Live, "LivePillFillBrush", "SuccessBrush")]
    [InlineData(PillKind.Reconnecting, "ReconnectingPillFillBrush", "WarningBrush")]
    [InlineData(PillKind.Neutral, "RaisedBrush", "TextMutedBrush")]
    public void Status_pill_colors_follow_its_kind(PillKind kind, string fill, string text)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var pill = new StatusPill { Kind = kind, Text = "x" };
            Assert.Same(Res(fill), pill.Background);
            Assert.Same(Res(text), ((TextBlock)pill.Child).Foreground);
        });
    }

    [Fact]
    public void Status_pill_has_round_ends_not_an_oval()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var pill = new StatusPill { Kind = PillKind.Live, Text = "● Live" };
            var host = new Border { Child = pill };
            host.Measure(new Size(400, 100));
            host.Arrange(new Rect(0, 0, 400, 100));
            host.UpdateLayout();
            // WPF shrinks oversized radii in proportion to both sides, so a huge radius draws an ellipse.
            // A capsule needs every corner at exactly half the height.
            double half = pill.ActualHeight / 2;
            Assert.True(pill.ActualHeight > 0);
            Assert.Equal(new CornerRadius(half), pill.CornerRadius);
        });
    }

    [Fact]
    public void Banner_close_button_shows_only_when_closable_and_raises_its_event()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var banner = new Banner { Kind = BannerKind.Error, Text = "The host ended the session." };
            Assert.Same(Res("ErrorBannerFillBrush"), banner.Background);
            Assert.Equal(Visibility.Collapsed, banner.CloseButton.Visibility);
            banner.CanClose = true;
            Assert.Equal(Visibility.Visible, banner.CloseButton.Visibility);
            bool closed = false;
            banner.CloseClicked += () => closed = true;
            banner.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(closed);
        });
    }

    [Fact]
    public void Step_tracker_marks_done_current_and_upcoming_steps()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var steps = new StepTracker { Current = 1 };
            Assert.Same(Res("PrimaryBrush"), steps.Dots[0].Fill);
            Assert.Same(Res("PrimaryTextBrush"), steps.Dots[1].Fill);
            Assert.Same(Res("BorderBrush"), steps.Dots[2].Fill);
        });
    }

    [Fact]
    public void Header_buttons_can_be_made_unfocusable_and_raise_controls()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var header = new AppHeader();
            header.ButtonsFocusable = false;
            Assert.False(header.ControlsButton.Focusable);
            Assert.False(header.HelpButton.Focusable);
            bool clicked = false;
            header.ControlsClicked += () => clicked = true;
            header.ControlsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(clicked);
        });
    }

    [Fact]
    public void A_danger_confirm_defaults_to_cancel()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new ThemedDialog(null, "Stop hosting?", "PC-07 will be disconnected.", "Stop hosting", "Keep hosting", danger: true);
            Assert.True(dialog.CancelButton.IsDefault);
            Assert.False(dialog.OkButton.IsDefault);
            Assert.Same(Res("DangerFilledButton"), dialog.OkButton.Style);
            dialog.Close();
        });
    }

    [Fact]
    public void An_alert_has_one_button()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new ThemedDialog(null, "Couldn't start hosting", "ViGEmBus is missing.", "OK", null, danger: false);
            Assert.Equal(Visibility.Collapsed, dialog.CancelButton.Visibility);
            Assert.True(dialog.OkButton.IsDefault);
            Assert.Equal("Couldn't start hosting", dialog.TitleText.Text);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(BannerKind.Info)]
    [InlineData(BannerKind.Warning)]
    [InlineData(BannerKind.Error)]
    public void Every_banner_kind_resolves_its_brushes(BannerKind kind)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            Assert.NotNull(Application.Current.TryFindResource($"{kind}BannerFillBrush"));
            Assert.NotNull(Application.Current.TryFindResource($"{kind}BannerBorderBrush"));
            Assert.NotNull(Application.Current.TryFindResource($"{kind}BannerTextBrush"));
            Assert.NotNull(new Banner { Kind = kind }.Background);
        });
    }
}
