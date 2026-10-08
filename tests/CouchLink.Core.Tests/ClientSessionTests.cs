using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class ClientSessionTests
{
    private sealed class RecordingClientEffects : IClientSessionEffects
    {
        public List<string> Calls { get; } = [];
        public Action? OnConnect { get; set; }

        public void Connect()
        {
            Calls.Add("connect");
            OnConnect?.Invoke();
        }

        public void Send(SessionMessage message) => Calls.Add($"send {message}");
        public void Disconnect() => Calls.Add("disconnect");
        public void StartPlaying(byte slot) => Calls.Add($"play P{slot}");
        public void Ended(string? message) => Calls.Add($"ended {message ?? "(left)"}");
        public void StateChanged() { }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingClientEffects _effects = new();
    private readonly ClientSession _session;

    public ClientSessionTests() => _session = new ClientSession(_effects, _time, hostName: "PC-03", ownName: "PC-07");

    private List<string> Calls => _effects.Calls;

    private void Pass(TimeSpan duration, bool hostAlive = false)
    {
        var step = TimeSpan.FromMilliseconds(250);
        for (var passed = TimeSpan.Zero; passed < duration; passed += step)
        {
            _time.Advance(duration - passed < step ? duration - passed : step);
            if (hostAlive)
                _session.Received(SessionMessage.Heartbeat);
            _session.Tick();
        }
    }

    private void Playing(byte slot = 4)
    {
        _session.Start();
        _session.Connected();
        _session.Received(SessionMessage.Accepted(slot));
        Calls.Clear();
    }

    [Fact]
    public void Start_connects_then_asks_to_join()
    {
        _session.Start();
        Assert.Equal(["connect"], Calls);
        Assert.Equal(ClientState.Connecting, _session.State);

        _session.Connected();
        Assert.Equal(["connect", "send JoinRequest PC-07"], Calls);
        Assert.Equal(ClientState.Waiting, _session.State);
    }

    [Fact]
    public void A_failed_connection_ends_with_could_not_reach()
    {
        _session.Start();
        _session.ConnectFailed();
        Assert.Equal(["connect", "disconnect", "ended Couldn't reach PC-03."], Calls);
        Assert.Equal(ClientState.Ended, _session.State);
    }

    [Fact]
    public void Accepted_starts_playing_in_that_slot()
    {
        _session.Start();
        _session.Connected();
        _session.Received(SessionMessage.Accepted(4));
        Assert.Equal("play P4", Calls[^1]);
        Assert.Equal(ClientState.Playing, _session.State);
        Assert.Equal(4, _session.Slot);
    }

    [Theory]
    [InlineData(DenyReason.Denied, "Request denied.")]
    [InlineData(DenyReason.Full, "Host is full.")]
    [InlineData(DenyReason.TimedOut, "The host didn't answer.")]
    [InlineData(DenyReason.PadFailed, "The host couldn't add a controller for you.")]
    public void Each_denial_has_its_message(DenyReason reason, string message)
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Received(SessionMessage.Denied(reason));
        Assert.Equal(["disconnect", $"ended {message}"], Calls);
    }

    [Fact]
    public void Cancel_while_waiting_tells_the_host_and_ends_with_no_message()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Leave();
        Assert.Equal(["send Leave", "disconnect", "ended (left)"], Calls);
    }

    [Fact]
    public void Losing_the_connection_while_waiting_ends_with_lost_the_host()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Disconnected();
        Assert.Equal(["disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Five_seconds_of_host_silence_while_waiting_ends_the_wait()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        Pass(ClientSession.SilenceLimit);
        Assert.Equal(["disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Heartbeats_keep_a_session_playing()
    {
        Playing();
        Pass(TimeSpan.FromSeconds(20), hostAlive: true);
        Assert.Empty(Calls);
        Assert.Equal(ClientState.Playing, _session.State);
    }

    [Fact]
    public void Silence_while_playing_reconnects_and_resumes_the_same_slot()
    {
        Playing(4);
        Pass(ClientSession.SilenceLimit - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["disconnect", "connect"], Calls);
        Assert.Equal(ClientState.Reconnecting, _session.State);

        _session.Connected();
        _session.Received(SessionMessage.Accepted(4));

        Assert.Equal(["disconnect", "connect", "send JoinRequest PC-07"], Calls); // no second "play"
        Assert.Equal(ClientState.Playing, _session.State);
    }

    [Fact]
    public void A_dropped_connection_while_playing_reconnects()
    {
        Playing();
        _session.Disconnected();
        Assert.Equal(["disconnect", "connect"], Calls);
        Assert.Equal(ClientState.Reconnecting, _session.State);
    }

    [Fact]
    public void Reconnecting_retries_every_second_and_gives_up_at_10s()
    {
        Playing();
        _effects.OnConnect = _session.ConnectFailed; // the host is unreachable
        _session.Disconnected();

        Pass(ClientSession.GiveUpAfter - TimeSpan.FromMilliseconds(250));
        Assert.Equal(ClientState.Reconnecting, _session.State);
        Assert.Equal(10, Calls.Count(c => c == "connect")); // at 0, 1, ... 9 s

        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal("ended Lost the host.", Calls[^1]);
        Assert.Equal(10, Calls.Count(c => c == "connect"));
    }

    [Fact]
    public void Coming_back_to_a_different_slot_ends_the_session()
    {
        Playing(4);
        _session.Disconnected();
        _session.Connected();
        Calls.Clear();
        _session.Received(SessionMessage.Accepted(5));
        Assert.Equal(["send Leave", "disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Kicked_and_host_ended_have_their_messages()
    {
        Playing();
        _session.Received(SessionMessage.Kicked);
        Assert.Equal("ended You were removed by the host.", Calls[^1]);

        var other = new RecordingClientEffects();
        var session = new ClientSession(other, _time, "PC-03", "PC-07");
        session.Start();
        session.Connected();
        session.Received(SessionMessage.Accepted(2));
        session.Received(SessionMessage.HostEnded);
        Assert.Equal("ended Host ended the session.", other.Calls[^1]);
    }

    [Fact]
    public void Fail_tells_the_host_and_ends_with_the_reason()
    {
        Playing();
        _session.Fail("The video window could not start.");
        Assert.Equal(["send Leave", "disconnect", "ended The video window could not start."], Calls);
    }

    [Fact]
    public void Leaving_while_reconnecting_without_a_connection_sends_nothing()
    {
        Playing();
        _session.Disconnected();
        _session.ConnectFailed();
        Calls.Clear();
        _session.Leave();
        Assert.Equal(["disconnect", "ended (left)"], Calls);
    }

    [Fact]
    public void Ended_is_final()
    {
        Playing();
        _session.Received(SessionMessage.Kicked);
        Calls.Clear();
        _session.Received(SessionMessage.HostEnded);
        _session.Disconnected();
        _session.Leave();
        Pass(TimeSpan.FromSeconds(30));
        Assert.Empty(Calls);
    }
}
