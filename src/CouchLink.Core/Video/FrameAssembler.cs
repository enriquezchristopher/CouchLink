using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>
/// One complete encoded frame, ready for the decoder. <see cref="AssemblyTime"/> is from its first
/// packet to the one that completed it.
/// </summary>
public sealed record AssembledFrame(uint Number, bool Keyframe, byte[] Data, bool Paused = false, TimeSpan AssemblyTime = default);

/// <summary>
/// Receive counters for the F2 overlay. Loss is measured on data shards: parity shards that
/// arrive after a frame is already delivered are not tracked, so counting them would overstate loss.
/// </summary>
public readonly record struct VideoReceiveStats(
    long PacketsReceived,
    long FramesCompleted,
    long FramesLost,
    long DataShardsExpected,
    long DataShardsMissing,
    long ShardsRecovered)
{
    public double LossPercent => DataShardsExpected == 0 ? 0 : 100.0 * DataShardsMissing / DataShardsExpected;
}

/// <summary>
/// Client side: collects shard datagrams into frames and repairs missing data shards with FEC.
/// A frame is delivered as soon as every block has enough shards. Delivering frame N gives up
/// every older unfinished frame; packets for N or older are ignored from then on. A frame with
/// no new packet for <see cref="FrameTimeout"/> is given up too, so a loss is noticed even when
/// no newer frame follows. A new stream id (host restarted streaming) clears everything.
/// Not thread-safe.
/// </summary>
public sealed class FrameAssembler
{
    public const int MaxPendingFrames = 8;
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(250);

    private sealed class Block(int dataShards, int parityShards)
    {
        public int DataShards { get; } = dataShards;
        public int ParityShards { get; } = parityShards;
        public byte[]?[] Shards { get; } = new byte[]?[dataShards + parityShards];
        public int Received { get; set; }
        public bool Done { get; set; }
        public int Rebuilt { get; set; }

        public int DataPresent()
        {
            int n = 0;
            for (int i = 0; i < DataShards; i++)
                if (Shards[i] is not null)
                    n++;
            return n;
        }
    }

    private sealed class PendingFrame(VideoShardHeader first, TimeSpan now)
    {
        public uint Number { get; } = first.Frame;
        public bool Keyframe { get; } = first.Keyframe;
        public bool Paused { get; } = first.Paused;
        public uint Length { get; } = first.FrameLength;
        public Block?[] Blocks { get; } = new Block?[first.BlockCount];
        public int BlocksDone { get; set; }
        public TimeSpan FirstPacket { get; } = now;
        public TimeSpan LastPacket { get; set; } = now;
    }

    private readonly Action<uint>? _onLost;
    private readonly Dictionary<uint, PendingFrame> _pending = [];
    private ushort? _streamId;
    private uint? _lastFinished;
    private long _packets, _completed, _lost, _dataExpected, _dataMissing, _recovered;

    public FrameAssembler(Action<uint>? onLost = null) => _onLost = onLost;

    public VideoReceiveStats Stats => new(_packets, _completed, _lost, _dataExpected, _dataMissing, _recovered);

    public int PendingFrames => _pending.Count;

    /// <summary>Adds one datagram. Returns the frame it completed, or null.</summary>
    public AssembledFrame? Add(ReadOnlySpan<byte> packet, TimeSpan now)
    {
        if (!VideoShardPacket.TryParse(packet, out var h))
            return null;
        _packets++;

        if (_streamId != h.StreamId)
        {
            _streamId = h.StreamId;
            _pending.Clear();
            _lastFinished = null;
        }
        if (IsFinished(h.Frame))
            return null; // late: already delivered or given up

        if (!_pending.TryGetValue(h.Frame, out var frame))
        {
            if (_pending.Count >= MaxPendingFrames)
            {
                GiveUp(OldestFirst(_pending.Values)[0]);
                if (IsFinished(h.Frame))
                    return null; // this packet's frame was even older
            }
            frame = new PendingFrame(h, now);
            _pending[h.Frame] = frame;
        }
        else if (h.Keyframe != frame.Keyframe || h.Paused != frame.Paused || h.FrameLength != frame.Length || h.BlockCount != frame.Blocks.Length)
        {
            return null; // disagrees with the frame's first packet
        }

        var block = frame.Blocks[h.Block] ??= new Block(h.DataShards, h.ParityShards);
        if (block.DataShards != h.DataShards || block.ParityShards != h.ParityShards)
            return null;
        if (block.Done || block.Shards[h.Shard] is not null)
            return null; // block already complete, or a duplicate

        block.Shards[h.Shard] = VideoShardPacket.Payload(packet).ToArray();
        block.Received++;
        frame.LastPacket = now;

        if (block.Received < block.DataShards)
            return null;
        if (!CompleteBlock(block))
        {
            GiveUp(frame);
            return null;
        }
        if (++frame.BlocksDone < frame.Blocks.Length)
            return null;
        return Finish(frame);
    }

    /// <summary>Gives up frames that have had no new packet for <see cref="FrameTimeout"/>.</summary>
    public void AbandonStale(TimeSpan now)
    {
        foreach (var frame in OldestFirst(_pending.Values.Where(f => now - f.LastPacket >= FrameTimeout)))
            GiveUp(frame);
    }

    private bool IsFinished(uint frame) =>
        _lastFinished is { } last && unchecked((int)(frame - last)) <= 0;

    private bool CompleteBlock(Block block)
    {
        if (block.DataPresent() < block.DataShards)
        {
            var shards = new Memory<byte>[block.Shards.Length];
            var present = new bool[block.Shards.Length];
            for (int i = 0; i < shards.Length; i++)
            {
                present[i] = block.Shards[i] is not null;
                shards[i] = block.Shards[i] ??= new byte[VideoShardPacket.PayloadSize];
            }
            if (!ReedSolomon.For(block.DataShards, block.ParityShards).Reconstruct(shards, present, out int rebuilt))
                return false;
            block.Rebuilt = rebuilt;
            _recovered += rebuilt;
        }
        block.Done = true;
        return true;
    }

    private AssembledFrame? Finish(PendingFrame frame)
    {
        long dataShards = frame.Blocks.Sum(b => b!.DataShards);
        long capacity = dataShards * VideoShardPacket.PayloadSize;
        if (frame.Length > capacity || frame.Length <= capacity - VideoShardPacket.PayloadSize)
        {
            GiveUp(frame); // the header's length doesn't match the shards
            return null;
        }

        var data = new byte[frame.Length];
        int offset = 0;
        foreach (var block in frame.Blocks)
            for (int i = 0; i < block!.DataShards && offset < data.Length; i++)
            {
                int n = Math.Min(VideoShardPacket.PayloadSize, data.Length - offset);
                block.Shards[i].AsSpan(0, n).CopyTo(data.AsSpan(offset));
                offset += n;
            }

        foreach (var older in OldestFirst(_pending.Values.Where(p => unchecked((int)(p.Number - frame.Number)) < 0)))
            GiveUp(older);
        _pending.Remove(frame.Number);
        Account(frame);
        _completed++;
        _lastFinished = frame.Number;
        return new AssembledFrame(frame.Number, frame.Keyframe, data, frame.Paused, frame.LastPacket - frame.FirstPacket);
    }

    private void GiveUp(PendingFrame frame)
    {
        _pending.Remove(frame.Number);
        Account(frame);
        _lost++;
        if (!IsFinished(frame.Number))
            _lastFinished = frame.Number;
        _onLost?.Invoke(frame.Number);
    }

    private void Account(PendingFrame frame)
    {
        int knownDataShards = frame.Blocks.FirstOrDefault(b => b is not null)?.DataShards ?? 0;
        foreach (var block in frame.Blocks)
        {
            if (block is null)
            {
                // Not one packet of this block arrived; assume it was the size of the others.
                _dataExpected += knownDataShards;
                _dataMissing += knownDataShards;
                continue;
            }
            _dataExpected += block.DataShards;
            _dataMissing += block.Done ? block.Rebuilt : block.DataShards - block.DataPresent();
        }
    }

    /// <summary>Sorts frames oldest first, treating frame numbers as wrapping around.</summary>
    private static List<PendingFrame> OldestFirst(IEnumerable<PendingFrame> frames)
    {
        var list = frames.ToList();
        if (list.Count > 1)
        {
            uint reference = list[0].Number;
            list.Sort((x, y) => unchecked((int)(x.Number - reference)).CompareTo(unchecked((int)(y.Number - reference))));
        }
        return list;
    }
}
