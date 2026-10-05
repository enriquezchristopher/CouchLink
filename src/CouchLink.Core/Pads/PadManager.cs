using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Pads;

/// <summary>
/// Owns the host's virtual pads: one per slot (P2..P10), created on the first
/// packet for that slot. A pad that stops receiving packets is released to
/// Neutral so a crashed client can't leave a player running forever.
/// </summary>
public sealed class PadManager : IDisposable
{
    public const byte FirstSlot = 2;
    public const byte LastSlot = 10;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);

    private sealed class Entry(IVirtualPad pad)
    {
        public IVirtualPad Pad { get; } = pad;
        public long LastSeen { get; set; }
        public bool IsNeutral { get; set; }
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

    /// <summary>Applies a packet. Returns false if it was ignored.</summary>
    public bool Handle(InputPacket packet)
    {
        if (packet.Slot is < FirstSlot or > LastSlot)
            return false;

        lock (_gate)
        {
            if (!_filter.Accept(packet.Slot, packet.Epoch, packet.Sequence))
                return false;

            if (!_pads.TryGetValue(packet.Slot, out var entry))
            {
                entry = new Entry(_factory.Create());
                _pads[packet.Slot] = entry;
            }

            entry.Pad.Apply(packet.State);
            entry.LastSeen = _time.GetTimestamp();
            entry.IsNeutral = packet.State == PadState.Neutral;
            return true;
        }
    }

    /// <summary>Call periodically (e.g. every 100 ms).</summary>
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
