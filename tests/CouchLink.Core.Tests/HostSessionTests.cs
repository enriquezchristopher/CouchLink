using System.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

internal sealed class RecordingHostEffects : IHostSessionEffects
{
    public List<string> Calls { get; } = [];
    public bool PlugFails { get; set; }
    public int PlayersChangedCount { get; private set; }

    public bool PlugPad(byte slot, IPAddress address)
    {
        Calls.Add($"plug P{slot} {address}");
        return !PlugFails;
    }

    public void HoldPad(byte slot) => Calls.Add($"hold P{slot}");
    public void UnplugPad(byte slot) => Calls.Add($"unplug P{slot}");
    public void AddTarget(byte slot, IPAddress address) => Calls.Add($"target+ P{slot} {address}");
    public void RemoveTarget(byte slot) => Calls.Add($"target- P{slot}");
    public void AskHost(int connection, string name) => Calls.Add($"ask #{connection} {name}");
    public void CloseAsk(int connection) => Calls.Add($"close-ask #{connection}");
    public void PlayersChanged() => PlayersChangedCount++;
    public void Send(int connection, SessionMessage message) => Calls.Add($"send #{connection} {message}");
    public void Close(int connection) => Calls.Add($"close #{connection}");
}

public class HostSessionTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingHostEffects _effects = new();
    private readonly HostSession _session;

    public HostSessionTests() => _session = new HostSession(_effects, _time);

    private List<string> Calls => _effects.Calls;

    private void Join(int connection, string name = "PC-07", string ip = "10.0.0.7")
    {
        _session.Connected(connection, IPAddress.Parse(ip));
        _session.Received(connection, SessionMessage.JoinRequest(name));
    }

    private void JoinAllowed(int connection, string name = "PC-07", string ip = "10.0.0.7")
    {
        bool allow = _session.AllowEveryone;
        _session.AllowEveryone = true;
        Join(connection, name, ip);
        _session.AllowEveryone = allow;
    }

    /// <summary>Lets time pass in 250 ms steps, the server's tick rate; <paramref name="alive"/> send heartbeats.</summary>
    private void Pass(TimeSpan duration, params int[] alive)
    {
        var step = TimeSpan.FromMilliseconds(250);
        for (var passed = TimeSpan.Zero; passed < duration; passed += step)
        {
            _time.Advance(duration - passed < step ? duration - passed : step);
            foreach (var connection in alive)
                _session.Received(connection, SessionMessage.Heartbeat);
            _session.Tick();
        }
    }

    [Fact]
    public void With_allow_everyone_a_join_gets_the_lowest_slot_at_once()
    {
        _session.AllowEveryone = true;
        Join(1);
        Assert.Equal(["plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #1 Accepted P2"], Calls);
        Assert.Equal(new PlayerInfo(2, "PC-07", IPAddress.Parse("10.0.0.7"), PlayerState.Active), Assert.Single(_session.Players));
        Assert.Equal(1, _effects.PlayersChangedCount);
    }

    [Fact]
    public void Without_allow_everyone_the_host_is_asked()
    {
        Join(1);
        Assert.Equal(["ask #1 PC-07"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Allow_accepts_the_client()
    {
        Join(1);
        Calls.Clear();
        _session.Allow(1);
        Assert.Equal(["close-ask #1", "plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #1 Accepted P2"], Calls);
    }

    [Fact]
    public void Deny_tells_the_client_and_closes()
    {
        Join(1);
        Calls.Clear();
        _session.Deny(1);
        Assert.Equal(["close-ask #1", "send #1 Denied Denied", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void An_unanswered_ask_is_denied_after_30s()
    {
        Join(1);
        Calls.Clear();
        Pass(HostSession.AskTimeout - TimeSpan.FromMilliseconds(250), alive: 1);
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250), alive: 1);
        Assert.Equal(["close-ask #1", "send #1 Denied TimedOut", "close #1"], Calls);
    }

    [Fact]
    public void A_tenth_client_is_told_the_host_is_full()
    {
        for (int i = 1; i <= 9; i++)
            JoinAllowed(i, $"PC-{i}", $"10.0.0.{i}");
        Calls.Clear();
        Join(10, "PC-10", "10.0.0.10");
        Assert.Equal(["send #10 Denied Full", "close #10"], Calls);
        Assert.Equal(9, _session.PlayerCount);
    }

    [Fact]
    public void Allow_after_the_host_filled_up_denies_full()
    {
        Join(1, "PC-01", "10.0.0.1"); // waits on the popup
        for (int i = 2; i <= 10; i++)
            JoinAllowed(i, $"PC-{i}", $"10.0.0.{i}");
        Calls.Clear();
        _session.Allow(1);
        Assert.Equal(["close-ask #1", "send #1 Denied Full", "close #1"], Calls);
        Assert.Equal(9, _session.PlayerCount);
    }

    [Fact]
    public void Allow_for_a_connection_that_is_gone_does_nothing()
    {
        Join(1);
        _session.Disconnected(1);
        Calls.Clear();
        _session.Allow(1);
        _session.Deny(1);
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_freed_slot_is_the_next_one_given_out()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1");
        JoinAllowed(2, "PC-2", "10.0.0.2");
        JoinAllowed(3, "PC-3", "10.0.0.3");
        _session.Received(2, SessionMessage.Leave);
        Calls.Clear();
        JoinAllowed(4, "PC-4", "10.0.0.4");
        Assert.Equal("plug P3 10.0.0.4", Calls[0]);
    }

    [Fact]
    public void Leave_unplugs_and_frees_the_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.Leave);
        Assert.Equal(["unplug P2", "target- P2", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Kick_tells_the_client_unplugs_and_keeps_no_reservation()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Kick(2);
        Assert.Equal(["send #1 Kicked", "close #1", "unplug P2", "target- P2"], Calls);
        Assert.Empty(_session.Players);

        Calls.Clear();
        Join(2); // the same PC comes back: the host is asked again
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void Kick_on_a_reserved_slot_just_unplugs_it()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Calls.Clear();
        _session.Kick(2);
        Assert.Equal(["unplug P2"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Five_seconds_of_silence_reserves_the_slot_and_keeps_the_pad_plugged()
    {
        JoinAllowed(1);
        Calls.Clear();
        Pass(HostSession.SilenceLimit - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["hold P2", "target- P2", "close #1"], Calls);
        Assert.Equal(PlayerState.Reserved, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void Heartbeats_keep_a_client_in()
    {
        JoinAllowed(1);
        Calls.Clear();
        Pass(TimeSpan.FromSeconds(30), alive: 1);
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_dropped_connection_reserves_the_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Disconnected(1);
        Assert.Equal(["hold P2", "target- P2"], Calls);
        Assert.Equal(PlayerState.Reserved, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void The_same_name_rejoining_within_60s_gets_its_slot_back_without_asking()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Pass(HostSession.ReserveFor - TimeSpan.FromSeconds(1));
        Calls.Clear();
        Join(2, "PC-07", "10.0.0.9"); // new DHCP lease: a new address is fine
        Assert.Equal(["plug P2 10.0.0.9", "target+ P2 10.0.0.9", "send #2 Accepted P2"], Calls);
        Assert.Equal(new PlayerInfo(2, "PC-07", IPAddress.Parse("10.0.0.9"), PlayerState.Active), Assert.Single(_session.Players));
    }

    [Fact]
    public void A_reservation_expires_after_60s_and_unplugs_the_pad()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Calls.Clear();
        Pass(HostSession.ReserveFor - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["unplug P2"], Calls);
        Assert.Empty(_session.Players);

        Calls.Clear();
        Join(2);
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void Same_name_and_ip_takes_over_an_active_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        Join(2); // the client restarted before the host noticed the old connection was dead
        Assert.Equal(["close #1", "plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #2 Accepted P2"], Calls);
    }

    [Fact]
    public void The_old_connections_late_disconnect_changes_nothing()
    {
        JoinAllowed(1);
        Join(2);
        Calls.Clear();
        _session.Disconnected(1);
        _session.Received(1, SessionMessage.Leave);
        Assert.Empty(Calls);
        Assert.Equal(PlayerState.Active, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void Same_name_from_another_ip_while_active_goes_to_the_host()
    {
        JoinAllowed(1);
        Calls.Clear();
        Join(2, "PC-07", "10.0.0.99");
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void A_pending_client_that_disconnects_closes_its_popup()
    {
        Join(1);
        Calls.Clear();
        _session.Disconnected(1);
        Assert.Equal(["close-ask #1"], Calls);
    }

    [Fact]
    public void A_second_request_from_the_same_pc_replaces_the_first()
    {
        Join(1);
        Calls.Clear();
        Join(2);
        Assert.Equal(["close-ask #1", "close #1", "ask #2 PC-07"], Calls);
    }

    [Fact]
    public void A_join_request_twice_on_one_connection_is_ignored()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.JoinRequest("PC-07"));
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_client_sending_a_host_message_is_dropped()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.Accepted(3));
        Assert.Equal(["hold P2", "target- P2", "close #1"], Calls);
    }

    [Fact]
    public void A_pad_that_cannot_be_created_denies_the_join()
    {
        _effects.PlugFails = true;
        JoinAllowed(1);
        Assert.Equal(["plug P2 10.0.0.7", "send #1 Denied PadFailed", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Stop_ends_every_connection_and_unplugs_every_pad()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1"); // active on P2
        JoinAllowed(2, "PC-2", "10.0.0.2"); // P3, then reserved
        _session.Disconnected(2);
        Join(3, "PC-3", "10.0.0.3");        // pending
        Calls.Clear();

        _session.Stop();

        Assert.Equal(
            ["send #1 HostEnded", "close #1", "close-ask #3", "send #3 HostEnded", "close #3", "unplug P2", "target- P2", "unplug P3"],
            Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Players_lists_active_and_reserved_slots_in_order()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1");
        JoinAllowed(2, "PC-2", "10.0.0.2");
        _session.Disconnected(1);
        Assert.Equal(
            [new PlayerInfo(2, "PC-1", IPAddress.Parse("10.0.0.1"), PlayerState.Reserved),
             new PlayerInfo(3, "PC-2", IPAddress.Parse("10.0.0.2"), PlayerState.Active)],
            _session.Players);
        Assert.Equal(2, _session.PlayerCount);
    }
}
