using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public class HostSetupTests
{
    private static HostSetupView Setup(PadKind initial = PadKind.DualShock4)
    {
        ThemeManager.Install(Application.Current);
        return new HostSetupView(initial);
    }

    private static RadioButton Choice(HostSetupView view, PadKind kind) =>
        view.PadKindGroup.Children.OfType<RadioButton>().Single(r => (PadKind)r.Tag == kind);

    [Fact]
    public void It_starts_on_the_remembered_choice_with_its_help()
    {
        Wpf.Run(() =>
        {
            var view = Setup(PadKind.Xbox360);
            Assert.Equal(PadKind.Xbox360, view.Selected);
            Assert.Equal("Up to 4 players. For games that only work with Xbox controllers, like NBA 2K14.", view.HelpText.Text);
        });
    }

    [Fact]
    public void Switching_the_controller_changes_the_help()
    {
        Wpf.Run(() =>
        {
            var view = Setup();
            Assert.Equal("Up to 9 players. Works with most games, like NBA 2K22.", view.HelpText.Text);
            Choice(view, PadKind.Xbox360).IsChecked = true;
            Assert.Equal(PadKind.Xbox360, view.Selected);
            Assert.StartsWith("Up to 4 players.", view.HelpText.Text);
        });
    }

    [Fact]
    public void Start_hosting_asks_for_the_selected_kind_and_Back_goes_back()
    {
        Wpf.Run(() =>
        {
            var view = Setup();
            PadKind? started = null;
            bool back = false;
            view.StartRequested += k => started = k;
            view.BackClicked += () => back = true;
            Choice(view, PadKind.Xbox360).IsChecked = true;
            view.StartButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            view.BackButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(PadKind.Xbox360, started);
            Assert.True(back);
            Assert.True(view.StartButton.IsDefault);
        });
    }

    [Fact]
    public void A_failed_start_shows_the_error_and_keeps_the_choice()
    {
        Wpf.Run(() =>
        {
            var view = Setup(PadKind.Xbox360);
            Assert.Equal(Visibility.Collapsed, view.ErrorBanner.Visibility);
            view.ShowError("ViGEmBus driver not installed");
            Assert.Equal(Visibility.Visible, view.ErrorBanner.Visibility);
            Assert.Equal("ViGEmBus driver not installed", view.ErrorBanner.Text);
            Assert.Equal(PadKind.Xbox360, view.Selected);
        });
    }
}
