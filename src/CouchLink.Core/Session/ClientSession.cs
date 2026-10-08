using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

public enum ClientState { Connecting, Waiting, Playing, Reconnecting, Ended }

/// <summary>What the client session asks the app to do. Called with the session's lock held.</summary>
public interface IClientSessionEffects
{
    /// <summary>Starts one connection attempt; report back through Connected or ConnectFailed.</summary>
    void Connect();
    void Send(SessionMessage message);
    void Disconnect();
    void StartPlaying(byte slot);

    /// <summary>The session is over; <paramref name="message"/> says why, or is null when the user left.</summary>
    void Ended(string? message);
    void StateChanged();
}

/// <summary>
/// The client's side of one join: connect, ask, wait for the host, play. If the host goes quiet for
/// <see cref="SilenceLimit"/> or the connection drops while playing, it reconnects once a second and
/// asks again (the host kept the slot), giving up after <see cref="GiveUpAfter"/>. Video and input
/// keep running meanwhile; only the session channel is redone. Not thread-safe.
/// </summary>
public sealed class ClientSession
{
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);

    public const string RequestDenied = "Request denied.";
    public const string HostFull = "Host is full.";
    public const string NoAnswer = "The host didn't answer.";
    public const string PadFailed = "The host couldn't add a controller for you.";
    public const string LostHost = "Lost the host.";
    public const string KickedMessage = "You were removed by the host.";
    public const string HostEndedMessage = "Host ended the session.";

    public static string CouldNotReach(string host) => $"Couldn't reach {host}.";

    private readonly IClientSessionEffects _effects;
    private readonly TimeProvider _time;
    private readonly string _ownName;
    private bool _connected, _connecting;
    private long _lastHeard, _lostAt, _lastAttempt;

    public ClientSession(IClientSessionEffects effects, TimeProvider time, string hostName, string ownName)
    {
        _effects = effects;
        _time = time;
        HostName = hostName;
        _ownName = ownName;
    }

    public string HostName { get; }
    public ClientState State { get; private set; } = ClientState.Connecting;
    public byte Slot { get; private set; }

    private long Now => _time.GetTimestamp();

    private TimeSpan Since(long timestamp) => _time.GetElapsedTime(timestamp);

    public void Start() => Attempt();

    public void Connected()
    {
        if (State == ClientState.Ended)
            return;
        _connecting = false;
        _connected = true;
        _lastHeard = Now;
        _effects.Send(SessionMessage.JoinRequest(_ownName));
        if (State == ClientState.Connecting)
            SetState(ClientState.Waiting);
    }

    public void ConnectFailed()
    {
        if (State == ClientState.Ended)
            return;
        _connecting = false;
        if (State == ClientState.Connecting)
            End(CouldNotReach(HostName));
        // Reconnecting: Tick tries again.
    }

    public void Received(SessionMessage message)
    {
        if (State == ClientState.Ended || !_connected)
            return;
        _lastHeard = Now;
        switch (message.Type)
        {
            case SessionMessageType.Accepted when State == ClientState.Waiting:
                Slot = message.Slot;
                SetState(ClientState.Playing);
                _effects.StartPlaying(message.Slot);
                break;
            case SessionMessageType.Accepted when State == ClientState.Reconnecting:
                if (message.Slot == Slot)
                    SetState(ClientState.Playing);
                else
                    Quit(LostHost); // the host forgot us; another slot would be a different player
                break;
            case SessionMessageType.Denied when State == ClientState.Waiting:
                End(DenyMessage(message.Reason));
                break;
            case SessionMessageType.Denied when State == ClientState.Reconnecting:
                End(LostHost);
                break;
            case SessionMessageType.Kicked:
                End(KickedMessage);
                break;
            case SessionMessageType.HostEnded:
                End(HostEndedMessage);
                break;
        }
    }

    public void Disconnected()
    {
        if (State == ClientState.Ended)
            return;
        _connected = false;
        _connecting = false;
        if (State == ClientState.Waiting)
            End(LostHost);
        else if (State == ClientState.Playing)
            Reconnect();
    }

    /// <summary>The user left (Cancel, Leave, Ctrl+Alt+Q).</summary>
    public void Leave() => Quit(null);

    /// <summary>Playing could not start on this PC (e.g. the video window failed).</summary>
    public void Fail(string message) => Quit(message);

    /// <summary>Call every 250 ms.</summary>
    public void Tick()
    {
        switch (State)
        {
            case ClientState.Waiting or ClientState.Playing when _connected && Since(_lastHeard) >= SilenceLimit:
                if (State == ClientState.Waiting)
                    End(LostHost);
                else
                    Reconnect();
                break;
            case ClientState.Reconnecting when Since(_lostAt) >= GiveUpAfter:
                End(LostHost);
                break;
            case ClientState.Reconnecting when !_connected && !_connecting && Since(_lastAttempt) >= RetryEvery:
                Attempt();
                break;
        }
    }

    private void Attempt()
    {
        _connecting = true;
        _lastAttempt = Now;
        _effects.Connect();
    }

    private void Reconnect()
    {
        _connected = false;
        _lostAt = Now;
        SetState(ClientState.Reconnecting);
        _effects.Disconnect();
        Attempt();
    }

    private void Quit(string? message)
    {
        if (State == ClientState.Ended)
            return;
        if (_connected)
            _effects.Send(SessionMessage.Leave);
        End(message);
    }

    private void End(string? message)
    {
        if (State == ClientState.Ended)
            return;
        _connected = _connecting = false;
        State = ClientState.Ended;
        _effects.Disconnect();
        _effects.Ended(message);
        _effects.StateChanged();
    }

    private void SetState(ClientState state)
    {
        State = state;
        _effects.StateChanged();
    }

    private static string DenyMessage(DenyReason reason) => reason switch
    {
        DenyReason.Full => HostFull,
        DenyReason.TimedOut => NoAnswer,
        DenyReason.PadFailed => PadFailed,
        _ => RequestDenied,
    };
}
