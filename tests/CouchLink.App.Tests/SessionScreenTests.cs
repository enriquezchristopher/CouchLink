using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class SessionScreenTests
{
    private static SessionView View()
    {
        ThemeManager.Install(Application.Current);
        return new SessionView();
    }

    [Fact]
    public void Waiting_shows_step_two_a_spinner_and_cancel()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Waiting, "PORTAL-SERVER", 0);
            Assert.Equal(1, view.Steps.Current);
            Assert.Equal("Waiting for PORTAL-SERVER to let you in", view.Heading.Text);
            Assert.Equal("Cancel", view.LeaveButton.Content);
            Assert.Equal(Visibility.Visible, view.Busy.Visibility);
            Assert.Equal(Visibility.Collapsed, view.Chip.Visibility);
            Assert.Equal(Visibility.Collapsed, view.Shortcuts.Visibility);
        });
    }

    [Fact]
    public void Playing_shows_the_player_chip_and_the_shortcuts()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Playing, "PORTAL-SERVER", 3);
            Assert.Equal(2, view.Steps.Current);
            Assert.Equal("You're P3", view.Heading.Text);
            Assert.Equal("Leave", view.LeaveButton.Content);
            Assert.Equal(Visibility.Visible, view.Chip.Visibility);
            Assert.Equal(3, view.Chip.Slot);
            Assert.Equal(Visibility.Collapsed, view.Busy.Visibility);
            Assert.Equal(Visibility.Visible, view.Shortcuts.Visibility);
            Assert.Equal(Visibility.Collapsed, view.ReconnectingPill.Visibility);
        });
    }

    [Fact]
    public void Reconnecting_shows_the_pill()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Reconnecting, "PORTAL-SERVER", 3);
            Assert.Equal(Visibility.Visible, view.ReconnectingPill.Visibility);
            Assert.Equal("Leave", view.LeaveButton.Content);
        });
    }

    [Fact]
    public void Ended_keeps_what_is_shown()
    {
        Wpf.Run(() =>
        {
            var view = View();
            view.Show(ClientState.Playing, "PORTAL-SERVER", 3);
            view.Show(ClientState.Ended, "PORTAL-SERVER", 3);
            Assert.Equal("You're P3", view.Heading.Text);
        });
    }

    [Fact]
    public void Nothing_on_the_screen_can_take_keyboard_focus()
    {
        Wpf.Run(() =>
        {
            var view = View();
            Assert.False(view.LeaveButton.Focusable);
            Assert.False(view.ControlsButton.Focusable);
            Assert.False(view.DetailsExpander.Focusable);
        });
    }

    [Fact]
    public void Nothing_inside_the_expander_can_take_keyboard_focus()
    {
        Wpf.Run(() =>
        {
            var view = View();
            var host = new Border { Child = view };
            host.Measure(new Size(540, 600));
            host.Arrange(new Rect(0, 0, 540, 600));
            view.DetailsExpander.IsExpanded = true;
            view.DetailsExpander.ApplyTemplate();
            host.UpdateLayout();
            var focusable = new List<string>();
            void Walk(DependencyObject node)
            {
                if (node is UIElement { Focusable: true })
                    focusable.Add(node.GetType().Name);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    Walk(VisualTreeHelper.GetChild(node, i));
            }
            Walk(view.DetailsExpander);
            Assert.Empty(focusable);
        });
    }
}
