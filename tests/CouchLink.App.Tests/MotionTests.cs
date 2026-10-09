using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.App.Views;
using CouchLink.Core.Video;

namespace CouchLink.App.Tests;

/// <summary>The motion pass: Reduce motion, the transitions that must stay smooth, and the closes they must not break.</summary>
public partial class MotionTests
{
    private static Window Offscreen(object content, double width = 540, double height = 600) => new()
    {
        Content = content,
        Width = width,
        Height = height,
        Left = -20000,
        Top = -20000,
        ShowActivated = false,
        ShowInTaskbar = false,
    };

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    [Fact]
    public void Reduce_motion_swaps_the_theme_durations_live()
    {
        Wpf.WithMotion(false, () =>
        {
            var app = Application.Current;
            Assert.False(Motion.Reduced);
            Assert.Equal(TimeSpan.FromMilliseconds(150), ((Duration)app.FindResource("MotionHover")).TimeSpan);
            Assert.Equal(0.97, (double)app.FindResource("PressScale"));
            var button = new Button();
            button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            var plain = new CheckBox();
            var panel = new StackPanel();
            panel.Children.Add(button);
            panel.Children.Add(plain);
            var window = Offscreen(panel, 300, 200);
            try
            {
                window.Show();
                var before = (button.Style, plain.Style);

                ThemeManager.SetReducedMotion(true);

                Assert.True(Motion.Reduced);
                Assert.True(((Duration)app.FindResource("MotionHover")).TimeSpan <= TimeSpan.FromMilliseconds(120));
                Assert.Equal(TimeSpan.Zero, ((Duration)app.FindResource("MotionMove")).TimeSpan);
                Assert.Equal(1.0, (double)app.FindResource("PressScale")); // no scaling at all
                // Shown controls pick up the reloaded styles, whose storyboards read the new values.
                Assert.True(Wpf.PumpUntil(() => !ReferenceEquals(before.Item1, button.Style) && !ReferenceEquals(before.Item2, plain.Style)));
                Assert.Same(app.FindResource("PrimaryButton"), button.Style);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_theme_follows_the_saved_setting()
    {
        Wpf.WithMotion(false, () =>
        {
            var settings = new MotionSettings(Path.Combine(Path.GetTempPath(), $"couchlink-{Guid.NewGuid():N}.json"), _ => { });
            using (ThemeManager.Follow(settings))
            {
                settings.ReduceMotion = true;
                Assert.True(Motion.Reduced);
                settings.ReduceMotion = false;
                Assert.False(Motion.Reduced);
            }
            settings.ReduceMotion = true; // no longer followed
            Assert.False(Motion.Reduced);
        });
    }

    [Fact]
    public void The_help_menu_has_a_checkable_reduce_motion_item_bound_to_the_setting()
    {
        Wpf.WithMotion(false, () =>
        {
            var saved = AppServices.MotionSettings;
            var settings = new MotionSettings(Path.Combine(Path.GetTempPath(), $"couchlink-{Guid.NewGuid():N}.json"), _ => { });
            AppServices.MotionSettings = settings;
            try
            {
                using var follow = ThemeManager.Follow(settings);
                var item = new AppHeader().ReduceMotionItem;
                Assert.Equal("Reduce motion", item.Header);
                Assert.Equal("HelpReduceMotion", AutomationProperties.GetAutomationId(item));
                Assert.True(item.IsCheckable);
                Assert.False(item.IsChecked);

                item.IsChecked = true; // what a click does
                Assert.True(settings.ReduceMotion);
                Assert.True(Motion.Reduced);

                settings.ReduceMotion = false;
                Assert.False(item.IsChecked);
            }
            finally
            {
                AppServices.MotionSettings = saved;
            }
        });
    }

    [GeneratedRegex(@"TargetProperty=""\(?(\w+\.)?(Width|Height|MinWidth|MinHeight|MaxWidth|MaxHeight|Margin|Padding|HorizontalAlignment|VerticalAlignment)\)?""")]
    private static partial Regex XamlLayoutTarget();

    [GeneratedRegex(@"BeginAnimation\(\s*[\w.]*(Width|Height|Margin|Padding|Alignment)Property")]
    private static partial Regex CodeLayoutTarget();

    [Fact]
    public void Nothing_animates_a_layout_property()
    {
        var offenders = Directory.GetFiles(RepoFiles.AppSource, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f)
                .Where(line => XamlLayoutTarget().IsMatch(line) || CodeLayoutTarget().IsMatch(line) || line.Contains("ThicknessAnimation"))
                .Select(line => $"{Path.GetFileName(f)}: {line.Trim()}"))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_layout_guard_catches_what_it_should()
    {
        Assert.Matches(XamlLayoutTarget(), "<DoubleAnimation Storyboard.TargetProperty=\"Width\" To=\"10\"/>");
        Assert.Matches(XamlLayoutTarget(), "Storyboard.TargetProperty=\"(FrameworkElement.Height)\"");
        Assert.Matches(CodeLayoutTarget(), "bar.BeginAnimation(FrameworkElement.WidthProperty, a);");
        Assert.DoesNotMatch(XamlLayoutTarget(), "Storyboard.TargetProperty=\"Opacity\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_quality_indicator_slides_to_the_checked_item(bool reduced)
    {
        Wpf.WithMotion(reduced, () =>
        {
            var lobby = new HostLobbyView();
            var window = Offscreen(lobby);
            try
            {
                window.Show();
                window.UpdateLayout();
                var items = lobby.QualityGroup.Children.OfType<RadioButton>().ToList();
                var balanced = items.Single(r => r.IsChecked == true);
                var indicator = lobby.QualityIndicator;
                Assert.Equal(balanced.TranslatePoint(new Point(), lobby.QualityGroup).X, indicator.Slide.X, 0.5);
                Assert.Equal(balanced.ActualWidth, indicator.ActualWidth, 0.5);

                var high = items.Single(r => (StreamQuality)r.Tag == StreamQuality.High);
                high.IsChecked = true;
                double highX = high.TranslatePoint(new Point(), lobby.QualityGroup).X;
                Assert.NotEqual(0, highX);
                Assert.Equal(highX, indicator.TargetX, 0.5);
                if (reduced)
                    Assert.Equal(highX, indicator.Slide.X, 0.5); // jumps: no movement
                else
                {
                    Assert.True(Math.Abs(indicator.Slide.X - highX) > 0.5); // not there yet
                    Assert.True(Wpf.PumpUntil(() => Math.Abs(indicator.Slide.X - highX) < 0.5)); // slides there
                }
                Assert.Equal(StreamQuality.High, lobby.CurrentSettings().Quality);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class RecordingView : UserControl, IDisposable
    {
        public RecordingView() => Content = new Border { Background = Brushes.Gray, Width = 200, Height = 200 };

        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_screen_change_disposes_the_old_view_at_once(bool reduced)
    {
        Wpf.WithMotion(reduced, () =>
        {
            var main = new MainWindow { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                main.Show();
                main.UpdateLayout();
                var first = new RecordingView();
                main.Show(first);
                main.UpdateLayout();
                var second = new RecordingView();
                main.Show(second);
                Assert.Equal(1, first.Disposed); // frees whatever it owned (UDP ports) now, not after the fade
                Assert.Null(VisualTreeHelper.GetParent(first));
                Assert.Equal(0, second.Disposed);
                Assert.Same(second, main.Screen.Content);
                Assert.True(Wpf.PumpUntil(() => second.Opacity == 1 && main.ScreenSnapshot.Source is null));
            }
            finally
            {
                main.Close();
            }
        });
    }

    private static bool? ShowAndPress(ThemedDialog dialog, Func<ThemedDialog, Button> button)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = dialog.Top = -20000;
        dialog.ShowActivated = false;
        dialog.ContentRendered += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Invoke(button(dialog)));
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        guard.Tick += (_, _) =>
        {
            guard.Stop();
            dialog.Close();
            Assert.Fail("The dialog did not close");
        };
        guard.Start();
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            guard.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dialog_still_returns_its_answer_after_the_close_fade(bool reduced)
    {
        Wpf.WithMotion(reduced, () =>
        {
            Assert.True(ShowAndPress(new ThemedDialog(null, "Stop hosting?", "Sure?", "Stop", "Keep", danger: false), d => d.OkButton));
            Assert.False(ShowAndPress(new ThemedDialog(null, "Stop hosting?", "Sure?", "Stop", "Keep", danger: false), d => d.CancelButton));
        });
    }

    [Fact]
    public void A_dialog_closed_mid_fade_closes_once()
    {
        Wpf.WithMotion(false, () =>
        {
            var dialog = new ThemedDialog(null, "t", "m", "OK", "Cancel", danger: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
            };
            int closed = 0;
            dialog.Closed += (_, _) => closed++;
            dialog.Show();
            Assert.True(Wpf.PumpUntil(() => dialog.IsLoaded));
            dialog.Close();
            Assert.True(dialog.IsVisible); // still fading
            dialog.Close(); // a second close during the fade changes nothing
            Assert.True(Wpf.PumpUntil(() => closed > 0));
            Wpf.PumpFor(200);
            Assert.Equal(1, closed);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_approval_popup_denies_when_closed_unanswered_and_only_once(bool reduced)
    {
        Wpf.WithMotion(reduced, () =>
        {
            int allowed = 0, denied = 0;
            bool closed = false;
            var popup = new ApprovalPopup("PC-07", 0, () => allowed++, () => denied++);
            popup.Loaded += (_, _) => popup.Left = -20000; // after it placed itself in the corner
            popup.Closed += (_, _) => closed = true;
            popup.Show();
            Assert.True(Wpf.PumpUntil(() => popup.IsLoaded));
            popup.Close();
            Assert.True(Wpf.PumpUntil(() => closed));
            Assert.Equal(0, allowed);
            Assert.Equal(1, denied);

            closed = false;
            var answered = new ApprovalPopup("PC-08", 0, () => allowed++, () => denied++);
            answered.Loaded += (_, _) => answered.Left = -20000;
            answered.Closed += (_, _) => closed = true;
            answered.Show();
            Assert.True(Wpf.PumpUntil(() => answered.IsLoaded));
            Invoke(answered.AllowButton);
            Wpf.PumpUntil(() => allowed > 0);
            Invoke(answered.AllowButton); // a second click while it slides out
            Assert.True(Wpf.PumpUntil(() => closed));
            Assert.Equal(1, allowed);
            Assert.Equal(1, denied);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_toggle_knob_moves_with_a_transform(bool reduced)
    {
        Wpf.WithMotion(reduced, () =>
        {
            var toggle = new CheckBox { Content = "Let everyone in" };
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
            var window = Offscreen(toggle, 300, 100);
            try
            {
                window.Show();
                window.UpdateLayout();
                var knob = (FrameworkElement)toggle.Template.FindName("Knob", toggle);
                Assert.Equal(HorizontalAlignment.Left, knob.HorizontalAlignment);
                var start = knob.TranslatePoint(new Point(), toggle);
                toggle.IsChecked = true;
                if (!reduced)
                    Assert.True(knob.TranslatePoint(new Point(), toggle).X - start.X < 17.5); // on its way, not there yet
                Assert.True(Wpf.PumpUntil(() => knob.TranslatePoint(new Point(), toggle).X - start.X > 17.5));
                Assert.Equal(HorizontalAlignment.Left, knob.HorizontalAlignment); // the knob moved, not the layout
                toggle.IsChecked = false;
                Assert.True(Wpf.PumpUntil(() => Math.Abs(knob.TranslatePoint(new Point(), toggle).X - start.X) < 0.5));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_spinner_and_countdown_bar_move_with_reduce_motion_on()
    {
        Wpf.WithMotion(true, () =>
        {
            var spinner = new Spinner();
            var window = Offscreen(spinner, 100, 100);
            try
            {
                window.Show();
                Assert.True(spinner.Turn.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
            }
            var toast = new ApprovalPopup("PC-11", 0, () => { }, () => { });
            Assert.True(toast.CountdownScale.HasAnimatedProperties);
            Assert.Equal(TimeSpan.FromSeconds(30), toast.BarAnimation!.Duration.TimeSpan);
            toast.CloseByHost();
        });
    }
}
