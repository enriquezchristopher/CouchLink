using CouchLink.Core.Protocol;

namespace CouchLink.Core.Audio;

public enum PlayoutKind { Frame, Repaired, Conceal, Silence }

/// <summary>What to play for the next 5 ms. <see cref="Data"/> is the Opus frame for Frame and Repaired.</summary>
public readonly record struct Playout(PlayoutKind Kind, ReadOnlyMemory<byte> Data = default);

public readonly record struct JitterStats(
    long Received, long Repaired, long Concealed, long Late, long Discarded, long Underruns);

/// <summary>
/// Client side: orders audio frames by sequence and decides what plays every 5 ms. Playback starts
/// once <see cref="PrimeFrames"/> frames are queued. A missing frame is replaced by the copy that
/// the next packet carried (Repaired) when that arrived, else concealed, for up to
/// <see cref="MaxConcealFrames"/> in a row; after that it plays silence and primes again. A packet
/// for a frame already played or concealed is late and dropped. More than
/// <see cref="MaxDepthFrames"/> queued (the client stalled) drops the oldest down to
/// <see cref="PrimeFrames"/>. A new stream ID starts over. Not thread-safe.
/// </summary>
public sealed class JitterBuffer
{
    public const int PrimeFrames = 3;      // 15 ms
    public const int MaxConcealFrames = 4; // 20 ms
    public const int MaxDepthFrames = 12;  // 60 ms

    private readonly Dictionary<uint, (ReadOnlyMemory<byte> Data, bool Repaired)> _frames = [];
    private ushort? _streamId;
    private bool _started; // _next means something: earlier frames are late
    private uint _next;
    private uint _newest;
    private int _concealRun;
    private long _received, _repaired, _concealed, _late, _discarded, _underruns;

    public bool Playing { get; private set; }

    /// <summary>Frames from the next one to play to the newest received (0 while not playing).</summary>
    public int Depth => Playing && !Before(_newest, _next) ? (int)(_newest - _next + 1) : 0;

    public JitterStats Stats => new(_received, _repaired, _concealed, _late, _discarded, _underruns);

    /// <summary>Adds a received packet. Returns true if it starts a new stream (reset the decoder).</summary>
    public bool Add(AudioPacket packet)
    {
        bool newStream = _streamId != packet.StreamId;
        if (newStream)
            StartOver(packet.StreamId);
        _received++;

        if (IsLate(packet.Sequence))
        {
            _late++;
            return newStream;
        }
        Store(packet.Sequence, packet.Frame, repaired: false);

        uint previous = packet.Sequence - 1;
        if (!packet.Previous.IsEmpty && !IsLate(previous) && !_frames.ContainsKey(previous))
            Store(previous, packet.Previous, repaired: true);

        TryStart();
        TrimIfTooDeep();
        return newStream;
    }

    public Playout Next()
    {
        TryStart();
        if (!Playing)
            return new Playout(PlayoutKind.Silence);

        if (_frames.Remove(_next, out var frame))
        {
            _next++;
            _concealRun = 0;
            if (frame.Repaired)
                _repaired++;
            return new Playout(frame.Repaired ? PlayoutKind.Repaired : PlayoutKind.Frame, frame.Data);
        }
        if (_concealRun < MaxConcealFrames)
        {
            _next++;
            _concealRun++;
            _concealed++;
            return new Playout(PlayoutKind.Conceal);
        }

        Playing = false;
        _underruns++;
        return new Playout(PlayoutKind.Silence);
    }

    private bool IsLate(uint sequence) => _started && Before(sequence, _next);

    private void Store(uint sequence, ReadOnlyMemory<byte> data, bool repaired)
    {
        if (_frames.Count == 0 || Before(_newest, sequence))
            _newest = sequence;
        _frames[sequence] = (data, repaired);
    }

    private void TryStart()
    {
        if (Playing || _frames.Count == 0)
            return;
        uint oldest = _frames.Keys.Aggregate((a, b) => Before(a, b) ? a : b);
        if ((int)(_newest - oldest + 1) < PrimeFrames)
            return;

        _next = oldest;
        _started = true;
        _concealRun = 0;
        Playing = true;
    }

    private void TrimIfTooDeep()
    {
        if (Depth <= MaxDepthFrames)
            return;
        uint keepFrom = _newest - PrimeFrames + 1;
        foreach (var sequence in _frames.Keys.Where(s => Before(s, keepFrom)).ToList())
            _frames.Remove(sequence);
        _discarded += (int)(keepFrom - _next);
        _next = keepFrom;
    }

    private void StartOver(ushort streamId)
    {
        _streamId = streamId;
        _frames.Clear();
        _started = false;
        _concealRun = 0;
        Playing = false;
    }

    /// <summary>Sequence order that survives the 32-bit wrap.</summary>
    private static bool Before(uint a, uint b) => (int)(a - b) < 0;
}
