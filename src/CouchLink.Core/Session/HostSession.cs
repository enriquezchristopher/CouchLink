using System.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

public enum PlayerState
{
    Active,
    /// <summary>The client went silent or dropped; its slot and pad are kept for <see cref="HostSession.ReserveFor"/>.</summary>
    Reserved,
}

public readonly record struct PlayerInfo(byte Slot, string Name, IPAddress Address, PlayerState State);

/// <summary>
/// What the session asks the app to do. Called with the session's lock held: return quickly, and
/// never call back into the session (post to the UI thread instead).
/// </summary>
public interface IHostEffects
{
    /// <summary>Plugs in (or rebinds) the slot's pad for this address. False if the pad could not be created.</summary>
    bool PlugPad(byte slot, IPAddress address);
    void HoldPad(byte slot);
    void UnplugPad(byte slot);
    void AddTarget(byte slot, IPAddress address);
    void RemoveTarget(byte slot);
    void AskHost(int connection, string name);
    void CloseAsk(int connection);
    void PlayersChanged();
}

/// <summary>The app's effects plus the transport's.</summary>
public interface IHostSessionEffects : IHostEffects
{
    void Send(int connection, SessionMessage message);

    /// <summary>Closes the connection after what's queued is sent. The session has already forgotten it.</summary>
    void Close(int connection);
}

/// <summary>
/// The host's sessions: who is waiting on the popup, who has which slot, who is gone but may come
/// back. Every slot is free, active (a live connection) or reserved (held for
/// <see cref="ReserveFor"/> with its pad still plugged in, so the game keeps the player). A reserved
/// slot is reclaimed by PC name alone; an active one is taken over only by the same name and address.
/// Not thread-safe: the caller holds one lock around every call.
/// </summary>
public sealed class HostSession
{
    public const int Capacity = PadManager.LastSlot - PadManager.FirstSlot + 1;
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReserveFor = TimeSpan.FromSeconds(60);

    private sealed class Connection(int id, IPAddress address, long lastHeard)
    {
        public int Id { get; } = id;
        public IPAddress Address { get; } = address;
        public long LastHeard { get; set; } = lastHeard;
        public string? Name { get; set; }
        public long? AskedAt { get; set; } // set while the host is being asked
        public byte Slot { get; set; }     // 0: none
    }

    private sealed class Seat(byte slot, string name, IPAddress address)
    {
        public byte Slot { get; } = slot;
        public string Name { get; } = name;
        public IPAddress Address { get; set; } = address;
        public int? Connection { get; set; } // null while reserved
        public long ReservedAt { get; set; }
    }

    private readonly IHostSessionEffects _effects;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly Dictionary<int, Connection> _connections = [];
    private readonly SortedDictionary<byte, Seat> _seats = [];

    public HostSession(IHostSessionEffects effects, TimeProvider time, Action<string>? log = null)
    {
        _effects = effects;
        _time = time;
        _log = log;
    }

    /// <summary>Accept joins without asking the host.</summary>
    public bool AllowEveryone { get; set; }

    /// <summary>Active and reserved slots.</summary>
    public int PlayerCount => _seats.Count;

    public IReadOnlyList<PlayerInfo> Players =>
        _seats.Values.Select(s => new PlayerInfo(s.Slot, s.Name, s.Address,
            s.Connection is null ? PlayerState.Reserved : PlayerState.Active)).ToList();

    private long Now => _time.GetTimestamp();

    private TimeSpan Since(long timestamp) => _time.GetElapsedTime(timestamp);

    public void Connected(int connection, IPAddress address) =>
        _connections[connection] = new Connection(connection, address, Now);

    public void Received(int connection, SessionMessage message)
    {
        if (!_connections.TryGetValue(connection, out var c))
            return;
        c.LastHeard = Now;
        switch (message.Type)
        {
            case SessionMessageType.Heartbeat:
                break;
            case SessionMessageType.JoinRequest:
                if (c.Name is null)
                {
                    c.Name = message.Name;
                    Join(c);
                }
                break;
            case SessionMessageType.Leave:
                Leave(c);
                break;
            default:
                _log?.Invoke($"Session: {Describe(c)} sent {message}, which only a host sends; closing");
                Drop(c);
                _effects.Close(c.Id);
                break;
        }
    }

    public void Disconnected(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c))
            return;
        _log?.Invoke($"Session: {Describe(c)} disconnected");
        Drop(c);
    }

    public void Allow(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c) || c.AskedAt is null)
            return;
        c.AskedAt = null;
        _effects.CloseAsk(c.Id);
        _log?.Invoke($"Session: host allowed {Describe(c)}");
        Accept(c);
    }

    public void Deny(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c) || c.AskedAt is null)
            return;
        _log?.Invoke($"Session: host denied {Describe(c)}");
        Refuse(c, DenyReason.Denied);
    }

    public void Kick(byte slot)
    {
        if (!_seats.Remove(slot, out var seat))
            return;
        _log?.Invoke($"Session: host kicked {seat.Name} ({seat.Address}) from P{slot}");
        bool active = seat.Connection is not null;
        if (seat.Connection is { } id && _connections.Remove(id))
        {
            _effects.Send(id, SessionMessage.Kicked);
            _effects.Close(id);
        }
        _effects.UnplugPad(slot);
        if (active)
            _effects.RemoveTarget(slot);
        _effects.PlayersChanged();
    }

    /// <summary>Call every 250 ms: silence, unanswered asks and expired reservations.</summary>
    public void Tick()
    {
        foreach (var c in _connections.Values.Where(c => Since(c.LastHeard) >= SilenceLimit).ToList())
        {
            _log?.Invoke($"Session: {Describe(c)} silent for {SilenceLimit.TotalSeconds:0} s");
            Drop(c);
            _effects.Close(c.Id);
        }
        foreach (var c in _connections.Values.Where(c => c.AskedAt is { } at && Since(at) >= AskTimeout).ToList())
        {
            _log?.Invoke($"Session: no answer for {Describe(c)}; denied");
            Refuse(c, DenyReason.TimedOut);
        }
        foreach (var seat in _seats.Values.Where(s => s.Connection is null && Since(s.ReservedAt) >= ReserveFor).ToList())
        {
            _log?.Invoke($"Session: P{seat.Slot} ({seat.Name}) did not come back; pad unplugged");
            _seats.Remove(seat.Slot);
            _effects.UnplugPad(seat.Slot);
            _effects.PlayersChanged();
        }
    }

    /// <summary>Stop hosting: tell everyone, close everything, unplug every pad.</summary>
    public void Stop()
    {
        foreach (var c in _connections.Values)
        {
            if (c.AskedAt is not null)
                _effects.CloseAsk(c.Id);
            _effects.Send(c.Id, SessionMessage.HostEnded);
            _effects.Close(c.Id);
        }
        _connections.Clear();
        foreach (var seat in _seats.Values)
        {
            _effects.UnplugPad(seat.Slot);
            if (seat.Connection is not null)
                _effects.RemoveTarget(seat.Slot);
        }
        _seats.Clear();
        _effects.PlayersChanged();
        _log?.Invoke("Session: hosting stopped");
    }

    private void Join(Connection c)
    {
        string name = c.Name!;
        _log?.Invoke($"Session: {Describe(c)} asks to join");

        var reserved = _seats.Values.FirstOrDefault(s => s.Connection is null && SameName(s.Name, name));
        if (reserved is not null)
        {
            _log?.Invoke($"Session: {Describe(c)} rejoins P{reserved.Slot}");
            TakeSeat(c, reserved);
            return;
        }

        var active = _seats.Values.FirstOrDefault(s =>
            s.Connection is not null && SameName(s.Name, name) && s.Address.Equals(c.Address));
        if (active is not null)
        {
            int old = active.Connection!.Value;
            _connections.Remove(old);
            _effects.Close(old);
            _log?.Invoke($"Session: {Describe(c)} takes over P{active.Slot} from its old connection");
            TakeSeat(c, active);
            return;
        }

        foreach (var other in _connections.Values
                     .Where(o => o != c && o.AskedAt is not null && SameName(o.Name!, name) && o.Address.Equals(c.Address))
                     .ToList())
        {
            _connections.Remove(other.Id);
            _effects.CloseAsk(other.Id);
            _effects.Close(other.Id);
        }

        if (FreeSlot() is null)
        {
            _log?.Invoke($"Session: {Describe(c)} denied, host is full");
            Refuse(c, DenyReason.Full);
        }
        else if (AllowEveryone)
            Accept(c);
        else
        {
            c.AskedAt = Now;
            _effects.AskHost(c.Id, name);
        }
    }

    private void Accept(Connection c)
    {
        if (FreeSlot() is not { } slot)
        {
            Refuse(c, DenyReason.Full);
            return;
        }
        if (!_effects.PlugPad(slot, c.Address))
        {
            _log?.Invoke($"Session: could not create a pad for {Describe(c)}");
            Refuse(c, DenyReason.PadFailed);
            return;
        }
        _seats[slot] = new Seat(slot, c.Name!, c.Address) { Connection = c.Id };
        c.Slot = slot;
        _effects.AddTarget(slot, c.Address);
        _effects.Send(c.Id, SessionMessage.Accepted(slot));
        _effects.PlayersChanged();
        _log?.Invoke($"Session: {Describe(c)} is P{slot}");
    }

    /// <summary>Puts a connection into an existing seat (a rejoin or a takeover).</summary>
    private void TakeSeat(Connection c, Seat seat)
    {
        if (!_effects.PlugPad(seat.Slot, c.Address))
        {
            _seats.Remove(seat.Slot);
            _effects.UnplugPad(seat.Slot);
            _effects.RemoveTarget(seat.Slot);
            Refuse(c, DenyReason.PadFailed);
            _effects.PlayersChanged();
            return;
        }
        seat.Address = c.Address;
        seat.Connection = c.Id;
        c.Slot = seat.Slot;
        _effects.AddTarget(seat.Slot, c.Address);
        _effects.Send(c.Id, SessionMessage.Accepted(seat.Slot));
        _effects.PlayersChanged();
    }

    private void Refuse(Connection c, DenyReason reason)
    {
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        _connections.Remove(c.Id);
        _effects.Send(c.Id, SessionMessage.Denied(reason));
        _effects.Close(c.Id);
    }

    private void Leave(Connection c)
    {
        _log?.Invoke($"Session: {Describe(c)} left");
        _connections.Remove(c.Id);
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        if (c.Slot != 0 && _seats.TryGetValue(c.Slot, out var seat) && seat.Connection == c.Id)
        {
            _seats.Remove(c.Slot);
            _effects.UnplugPad(c.Slot);
            _effects.RemoveTarget(c.Slot);
            _effects.PlayersChanged();
        }
        _effects.Close(c.Id);
    }

    /// <summary>The connection is gone (silent, dropped or misbehaving). An active seat is reserved.</summary>
    private void Drop(Connection c)
    {
        _connections.Remove(c.Id);
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        if (c.Slot != 0 && _seats.TryGetValue(c.Slot, out var seat) && seat.Connection == c.Id)
        {
            seat.Connection = null;
            seat.ReservedAt = Now;
            _effects.HoldPad(c.Slot);
            _effects.RemoveTarget(c.Slot);
            _effects.PlayersChanged();
            _log?.Invoke($"Session: P{c.Slot} ({seat.Name}) reserved for {ReserveFor.TotalSeconds:0} s");
        }
    }

    private byte? FreeSlot()
    {
        for (byte slot = PadManager.FirstSlot; slot <= PadManager.LastSlot; slot++)
            if (!_seats.ContainsKey(slot))
                return slot;
        return null;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Describe(Connection c) => $"{c.Name ?? "?"} ({c.Address})";
}
