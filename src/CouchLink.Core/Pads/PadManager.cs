using System.Net;
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Pads;

/// <summary>
/// Owns the host's virtual pads: one per slot (P2..P10), plugged in by the session when a client is
/// let in and bound to that client's address. Input for a slot is applied only from its address. A
/// held slot (client gone, waiting for it to rejoin) stays plugged in at neutral and ignores input,
/// so the game keeps the player. A pad that stops receiving packets is released to Neutral so a
/// crashed client can't leave a player running forever.
/// </summary>
public sealed class PadManager : IDisposable
{
    public const byte FirstSlot = 2;
    public const byte LastSlot = 10;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>How often <see cref="ReleaseStale"/> should run; a silent pad is released within StaleAfter + CheckInterval.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(25);

    private sealed class Entry(IVirtualPad pad)
    {
        public IVirtualPad Pad { get; } = pad;
        public IPAddress? Address { get; set; } // null while held
        public long LastSeen { get; set; }
        public bool IsNeutral { get; set; } = true;
    }

    private readonly IVirtualPadFactory _factory;
    private readonly TimeProvider _time;
    private readonly SequenceFilter _filter = new();
    private readonly Dictionary<byte, Entry> _pads = [];
    private readonly Lock _gate = new();

    public PadManager(IVirtualPadFactory factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    public int Count
    {
        get { lock (_gate) return _pads.Count; }
    }

    public bool IsPlugged(byte slot)
    {
        lock (_gate)
            return _pads.ContainsKey(slot);
    }

    /// <summary>Plugs in a pad for the slot (or keeps its pad) and binds it to the client's address. False for a slot outside 2-10.</summary>
    public bool Plug(byte slot, IPAddress address)
    {
        if (slot is < FirstSlot or > LastSlot)
            return false;

        lock (_gate)
        {
            if (!_pads.TryGetValue(slot, out var entry))
            {
                entry = new Entry(_factory.Create());
                _pads[slot] = entry;
            }
            entry.Address = address;
            return true;
        }
    }

    /// <summary>The client is gone for now: center the pad, keep it plugged in, ignore input until <see cref="Plug"/>.</summary>
    public void Hold(byte slot)
    {
        lock (_gate)
        {
            if (!_pads.TryGetValue(slot, out var entry))
                return;
            entry.Address = null;
            entry.Pad.Apply(PadState.Neutral);
            entry.IsNeutral = true;
        }
    }

    public void Unplug(byte slot)
    {
        lock (_gate)
        {
            if (_pads.Remove(slot, out var entry))
                entry.Pad.Dispose();
        }
    }

    /// <summary>Applies a packet from <paramref name="from"/>. Returns false if it was ignored.</summary>
    public bool Handle(InputPacket packet, IPAddress from)
    {
        lock (_gate)
        {
            if (!_pads.TryGetValue(packet.Slot, out var entry) || entry.Address is null || !entry.Address.Equals(from))
                return false;
            if (!_filter.Accept(packet.Slot, packet.Epoch, packet.Sequence))
                return false;

            entry.Pad.Apply(packet.State);
            entry.LastSeen = _time.GetTimestamp();
            entry.IsNeutral = packet.State == PadState.Neutral;
            return true;
        }
    }

    /// <summary>Call every <see cref="CheckInterval"/>.</summary>
    public void ReleaseStale()
    {
        lock (_gate)
        {
            foreach (var entry in _pads.Values)
            {
                if (entry.IsNeutral || _time.GetElapsedTime(entry.LastSeen) < StaleAfter)
                    continue;
                entry.Pad.Apply(PadState.Neutral);
                entry.IsNeutral = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _pads.Values)
                entry.Pad.Dispose();
            _pads.Clear();
        }
    }
}
