using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: splits one encoded frame into shard datagrams. The frame is cut into 1200-byte
/// data shards, grouped into as few FEC blocks of at most 200 data shards as possible (sizes
/// differ by at most one), and each block gets <see cref="ParityPercent"/> (rounded up)
/// Reed-Solomon parity shards. Packets come out block by block, data before parity.
/// </summary>
public sealed class FramePacketizer
{
    public const int MaxDataShardsPerBlock = VideoShardPacket.MaxDataShardsPerBlock;
    public const int MinParityPercent = VideoShardPacket.MinParityPercent;
    public const int MaxParityPercent = VideoShardPacket.MaxParityPercent;
    public const int DefaultParityPercent = 20;

    public FramePacketizer(ushort streamId, int parityPercent = DefaultParityPercent)
    {
        if (parityPercent is < MinParityPercent or > MaxParityPercent)
            throw new ArgumentOutOfRangeException(
                nameof(parityPercent), $"Parity must be {MinParityPercent}-{MaxParityPercent}%.");
        StreamId = streamId;
        ParityPercent = parityPercent;
    }

    public ushort StreamId { get; }
    public int ParityPercent { get; }

    public static int ParityShardsFor(int dataShards, int parityPercent) =>
        VideoShardPacket.ParityShardsFor(dataShards, parityPercent);

    public List<byte[]> Packetize(uint frameNumber, ReadOnlySpan<byte> frame, bool keyframe, bool paused = false)
    {
        if (frame.IsEmpty)
            throw new ArgumentException("Frame is empty.", nameof(frame));
        if (frame.Length > VideoShardPacket.MaxFrameLength)
            throw new ArgumentException(
                $"A {frame.Length}-byte frame is over the {VideoShardPacket.MaxFrameLength}-byte limit.", nameof(frame));

        int totalData = VideoShardPacket.DataShardsFor((uint)frame.Length);
        int blockCount = VideoShardPacket.BlocksFor(totalData);

        var packets = new List<byte[]>();
        int firstShard = 0; // index of this block's first data shard within the frame
        for (int block = 0; block < blockCount; block++)
        {
            int k = VideoShardPacket.DataShardsInBlock(totalData, blockCount, block);
            int m = ParityShardsFor(k, ParityPercent);
            var shards = new Memory<byte>[k + m];
            for (int s = 0; s < k + m; s++)
            {
                var packet = new byte[VideoShardPacket.Size];
                VideoShardPacket.WriteHeader(packet, new VideoShardHeader(
                    StreamId, frameNumber, keyframe, (uint)frame.Length,
                    (byte)block, (byte)blockCount, (byte)s, (byte)k, (byte)m, paused));
                shards[s] = packet.AsMemory(VideoShardPacket.HeaderSize, VideoShardPacket.PayloadSize);
                if (s < k)
                {
                    int start = (firstShard + s) * VideoShardPacket.PayloadSize;
                    int end = Math.Min(start + VideoShardPacket.PayloadSize, frame.Length);
                    frame[start..end].CopyTo(shards[s].Span); // the rest of the last shard stays zero
                }
                packets.Add(packet);
            }
            ReedSolomon.For(k, m).Encode(shards);
            firstShard += k;
        }
        return packets;
    }
}
