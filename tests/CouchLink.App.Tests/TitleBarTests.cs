using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CouchLink.App.Theme;
using CouchLink.App.Ui;

namespace CouchLink.App.Tests;

public class TitleBarTests
{
    private static void WithHost(ResizeMode mode, Action<Window, CaptionButtons> test)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var buttons = new CaptionButtons();
            var window = new Window
            {
                ResizeMode = mode,
                Content = buttons,
                Width = 300,
                Height = 200,
                Left = -20000,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                Pump();
                test(window, buttons);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Pump(DispatcherPriority priority = DispatcherPriority.ApplicationIdle) =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, priority);

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Visuals(child))
                yield return deeper;
        }
    }

    [Fact]
    public void A_resizable_window_shows_all_three_caption_buttons()
    {
        foreach (var mode in new[] { ResizeMode.CanResize, ResizeMode.CanResizeWithGrip })
        {
            WithHost(mode, (_, bar) =>
            {
                Assert.Equal(Visibility.Visible, bar.MinimizeButton.Visibility);
                Assert.Equal(Visibility.Visible, bar.MaximizeButton.Visibility);
                Assert.Equal(Visibility.Visible, bar.CloseButton.Visibility);
            });
        }
    }

    [Fact]
    public void A_fixed_window_shows_only_close()
    {
        WithHost(ResizeMode.NoResize, (_, bar) =>
        {
            Assert.Equal(Visibility.Collapsed, bar.MinimizeButton.Visibility);
            Assert.Equal(Visibility.Collapsed, bar.MaximizeButton.Visibility);
            Assert.Equal(Visibility.Visible, bar.CloseButton.Visibility);
        });
    }

    [Fact]
    public void A_minimize_only_window_shows_minimize_and_close()
    {
        WithHost(ResizeMode.CanMinimize, (_, bar) =>
        {
            Assert.Equal(Visibility.Visible, bar.MinimizeButton.Visibility);
            Assert.Equal(Visibility.Collapsed, bar.MaximizeButton.Visibility);
            Assert.Equal(Visibility.Visible, bar.CloseButton.Visibility);
        });
    }

    [Fact]
    public void Caption_buttons_never_take_focus_and_stay_clickable_inside_the_caption()
    {
        WithHost(ResizeMode.CanResize, (_, bar) =>
        {
            foreach (var button in new[] { bar.MinimizeButton, bar.MaximizeButton, bar.CloseButton })
            {
                Assert.False(button.Focusable);
                Assert.False(button.IsTabStop);
                Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
            }
            Assert.Equal("CaptionMinimize", AutomationProperties.GetAutomationId(bar.MinimizeButton));
            Assert.Equal("CaptionMaximize", AutomationProperties.GetAutomationId(bar.MaximizeButton));
            Assert.Equal("CaptionClose", AutomationProperties.GetAutomationId(bar.CloseButton));
            Assert.Equal("Minimize", AutomationProperties.GetName(bar.MinimizeButton));
            Assert.Equal("Close", AutomationProperties.GetName(bar.CloseButton));
        });
    }

    private static void WithTitleBar(string title, Action<TitleBar> test)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var bar = new TitleBar();
            var window = new Window { Content = bar, Title = title, Left = -20000, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show();
                Pump();
                test(bar);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Nothing_in_a_title_bar_is_focusable()
    {
        WithTitleBar("Controls", bar =>
        {
            Assert.False(bar.Focusable);
            Assert.DoesNotContain(Visuals(bar).OfType<UIElement>(), e => e.Focusable);
            Assert.DoesNotContain(Visuals(bar).OfType<Control>(), c => c.IsTabStop);
        });
    }

    [Fact]
    public void The_title_bar_shows_the_window_title()
    {
        WithTitleBar("Save profile", bar => Assert.Equal("Save profile", bar.TitleText.Text));
    }

    [Fact]
    public void The_header_keeps_Controls_and_Help_clickable_and_the_logo_draggable()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var header = new AppHeader();
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(header.ControlsButton));
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(header.HelpButton));
            Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(header.LogoMark));
            Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(header.TitleText));
            Assert.Equal(48, header.Height);
        });
    }

    [Fact]
    public void The_windows_replace_the_native_caption_with_their_own_bars()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);

            var main = new MainWindow();
            var chrome = WindowChrome.GetWindowChrome(main);
            Assert.Equal(48, chrome.CaptionHeight);
            Assert.False(chrome.UseAeroCaptionButtons);
            Assert.Equal(SystemParameters.WindowResizeBorderThickness, chrome.ResizeBorderThickness);
            main.Close();

            var editor = new ControlsWindow();
            AssertSecondary(editor);
            editor.Close();

            var dialog = new ThemedDialog(null, "Title", "Message", "OK", null, danger: false);
            AssertSecondary(dialog);
            dialog.Close();

            var save = new SaveProfileDialog(null, null);
            AssertSecondary(save);
            save.Close();
        });
    }

    private static void AssertSecondary(Window window)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        Assert.NotNull(chrome);
        Assert.Equal(32, chrome.CaptionHeight);
        Assert.Equal(new Thickness(0), chrome.ResizeBorderThickness);
        Assert.Single(LogicalTree(window).OfType<TitleBar>());
    }

    private static IEnumerable<object> LogicalTree(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject d)
                foreach (var deeper in LogicalTree(d))
                    yield return deeper;
        }
    }

    [Fact]
    public void The_maximize_button_becomes_restore_while_maximized()
    {
        WithHost(ResizeMode.CanResize, (window, bar) =>
        {
            Assert.Equal("Maximize", AutomationProperties.GetName(bar.MaximizeButton));
            var normalGlyph = bar.MaximizeButton.Content;

            window.WindowState = WindowState.Maximized;
            Pump();
            Assert.Equal("Restore", AutomationProperties.GetName(bar.MaximizeButton));
            Assert.NotSame(normalGlyph, bar.MaximizeButton.Content);

            window.WindowState = WindowState.Normal;
            Pump();
            Assert.Equal("Maximize", AutomationProperties.GetName(bar.MaximizeButton));
            Assert.Same(normalGlyph, bar.MaximizeButton.Content);
        });
    }

    [Fact]
    public void Clicking_close_closes_the_window()
    {
        WithHost(ResizeMode.CanResize, (window, bar) =>
        {
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            bar.CloseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var stop = DateTime.UtcNow.AddSeconds(5);
            while (!closed && DateTime.UtcNow < stop)
                Pump(DispatcherPriority.Background);
            Assert.True(closed);
        });
    }
}
