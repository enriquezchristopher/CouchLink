using System.Net;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class JoinListTests
{
    private static JoinListView List()
    {
        ThemeManager.Install(Application.Current);
        return new JoinListView();
    }

    [Fact]
    public void A_message_shows_in_the_error_banner_and_closes()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowMessage("The host ended the session.");
            Assert.Equal(Visibility.Visible, list.MessageBar.Visibility);
            Assert.Equal("The host ended the session.", list.MessageBar.Text);
            list.MessageBar.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Visibility.Collapsed, list.MessageBar.Visibility);
        });
    }

    [Fact]
    public void A_bad_address_shows_the_error_under_the_field_and_does_not_join()
    {
        Wpf.Run(() =>
        {
            var list = List();
            bool joined = false;
            list.JoinRequested += (_, _) => joined = true;
            list.AddressBox.Text = "192.168";
            list.JoinByAddress();
            Assert.False(joined);
            Assert.Equal(Visibility.Visible, list.AddressError.Visibility);
            Assert.Equal("Enter an IP address like 192.168.1.23.", list.AddressError.Text);
        });
    }

    [Fact]
    public void A_good_address_joins_and_clears_the_error()
    {
        Wpf.Run(() =>
        {
            var list = List();
            IPAddress? joined = null;
            list.JoinRequested += (address, _) => joined = address;
            list.AddressBox.Text = "1";
            list.JoinByAddress();
            list.AddressBox.Text = " 192.168.1.23 ";
            list.JoinByAddress();
            Assert.Equal(IPAddress.Parse("192.168.1.23"), joined);
            Assert.Equal(Visibility.Collapsed, list.AddressError.Visibility);
        });
    }

    [Fact]
    public void Hosts_become_cards_and_incompatible_ones_cannot_be_clicked()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowHosts(
            [
                new FoundHost("PORTAL-SERVER", IPAddress.Parse("192.168.1.10"), 2, 9, true),
                new FoundHost("PC-09", IPAddress.Parse("192.168.1.19"), 0, 9, false),
            ]);
            var cards = list.HostButtons.Children.OfType<Button>().ToList();
            Assert.Equal(2, cards.Count);
            Assert.True(cards[0].IsEnabled);
            Assert.False(cards[1].IsEnabled);
            Assert.Equal("PORTAL-SERVER, 3 of 10 players", System.Windows.Automation.AutomationProperties.GetName(cards[0]));
        });
    }

    [Fact]
    public void The_help_card_shows_only_while_no_host_was_found()
    {
        Wpf.Run(() =>
        {
            var list = List();
            list.ShowNoHostsHelp();
            Assert.Equal(Visibility.Visible, list.HelpCard.Visibility);
            list.ShowHosts([new FoundHost("PC-02", IPAddress.Loopback, 0, 9, true)]);
            Assert.Equal(Visibility.Collapsed, list.HelpCard.Visibility);
            list.ShowNoHostsHelp();
            Assert.Equal(Visibility.Collapsed, list.HelpCard.Visibility);
        });
    }
}
