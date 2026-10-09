using System.Net;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.App.Views;
using CouchLink.Core.Session;
using CouchLink.Core.Video;

namespace CouchLink.App.Tests;

public class HostLobbyTests
{
    private static HostLobbyView Lobby()
    {
        ThemeManager.Install(Application.Current);
        return new HostLobbyView();
    }

    [Fact]
    public void Quality_starts_on_the_default_and_follows_the_segmented_control()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            Assert.Equal(StreamSettings.Default.Quality, lobby.CurrentSettings().Quality);
            var high = lobby.QualityGroup.Children.OfType<RadioButton>().Single(r => (StreamQuality)r.Tag == StreamQuality.High);
            high.IsChecked = true;
            Assert.Equal(StreamQuality.High, lobby.CurrentSettings().Quality);
        });
    }

    [Fact]
    public void With_no_players_the_host_row_and_a_hint_show()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            lobby.ShowPlayers([]);
            Assert.Equal($"PLAYERS 1 / {HostSession.Capacity + 1}", lobby.PlayersHeading.Text);
            Assert.Contains(Descendants<TextBlock>(lobby.PlayerList), t => t.Text == "You (host)");
            Assert.Contains(Descendants<TextBlock>(lobby.PlayerList), t => t.Text == "No one has joined yet. Players appear here when they join.");
        });
    }

    [Fact]
    public void Players_get_their_chip_and_reconnecting_players_a_pill()
    {
        Wpf.Run(() =>
        {
            var lobby = Lobby();
            lobby.ShowPlayers(
            [
                new PlayerInfo(2, "PC-07", IPAddress.Loopback, PlayerState.Active),
                new PlayerInfo(3, "PC-11", IPAddress.Loopback, PlayerState.Reserved),
            ]);
            Assert.Equal($"PLAYERS 3 / {HostSession.Capacity + 1}", lobby.PlayersHeading.Text);
            Assert.Equal(new[] { 1, 2, 3 }, Descendants<PlayerChip>(lobby.PlayerList).Select(c => (int)c.Slot));
            Assert.Single(Descendants<StatusPill>(lobby.PlayerList), p => p.Text == "Reconnecting");
            Assert.Equal(2, Descendants<Button>(lobby.PlayerList).Count(b => (string)b.Content == "Kick"));
        });
    }

    private static IEnumerable<T> Descendants<T>(Panel panel) where T : DependencyObject =>
        panel.Children.OfType<DependencyObject>().SelectMany(Walk).OfType<T>();

    private static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var d in Walk(child))
                yield return d;
    }
}
