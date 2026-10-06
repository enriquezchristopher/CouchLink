using System.Buffers.Binary;
using CouchLink.Core.Fec;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Header of one video shard datagram. A frame is cut into 1200-byte data shards, grouped into
/// FEC blocks; each block carries <see cref="DataShards"/> data and <see cref="ParityShards"/>
/// parity shards. <see cref="StreamId"/> is random per host stream, so a client can tell a
/// restarted stream (frame numbers back at 0) from late packets.
/// </summary>
public readonly record struct VideoShardHeader(
    ushort StreamId,
    uint Frame,
    bool Keyframe,
    uint FrameLength,
    byte Block,
    byte BlockCount,
    byte Shard,
    byte DataShards,
    byte ParityShards,
    bool Paused = false)
{
    public int TotalShards => DataShards + ParityShards;
}

/// <summary>Host -> client video shard datagram: 20-byte header + 1200-byte payload.</summary>
public static class VideoShardPacket
{
    public const int HeaderSize = 20;
    public const int PayloadSize = 1200;
    public const int Size = HeaderSize + PayloadSize;

    /// <summary>Largest frame a client accepts (a 1080p keyframe is well under 1 MB); bounds memory for forged headers.</summary>
    public const uint MaxFrameLength = 4 * 1024 * 1024;

    /// <summary>At most this many data shards per FEC block.</summary>
    public const int MaxDataShardsPerBlock = 200;

    /// <summary>Parity per block is 10-20% of its data shards, rounded up.</summary>
    public const int MinParityPercent = 10;
    public const int MaxParityPercent = 20;

    private const byte FlagKeyframe = 1;
    private const byte FlagPaused = 2;

    /// <summary>Data shards needed for a frame of this many bytes.</summary>
    public static int DataShardsFor(uint frameLength) => (int)((frameLength + PayloadSize - 1) / PayloadSize);

    /// <summary>FEC blocks for this many data shards: as few as possible.</summary>
    public static int BlocksFor(int dataShards) => (dataShards + MaxDataShardsPerBlock - 1) / MaxDataShardsPerBlock;

    /// <summary>Data shards in one block; block sizes differ by at most one, larger blocks first.</summary>
    public static int DataShardsInBlock(int dataShards, int blockCount, int block) =>
        dataShards / blockCount + (block < dataShards % blockCount ? 1 : 0);

    public static int ParityShardsFor(int dataShards, int parityPercent) => (dataShards * parityPercent + 99) / 100;

    public static void WriteHeader(Span<byte> packet, in VideoShardHeader header)
    {
        if (packet.Length < HeaderSize)
            throw new ArgumentException($"Need {HeaderSize} bytes.", nameof(packet));

        Wire.WriteHeader(packet, Wire.TypeVideoShard);
        packet[4] = (byte)((header.Keyframe ? FlagKeyframe : 0) | (header.Paused ? FlagPaused : 0));
        packet[5] = header.Block;
        packet[6] = header.BlockCount;
        packet[7] = header.Shard;
        packet[8] = header.DataShards;
        packet[9] = header.ParityShards;
        BinaryPrimitives.WriteUInt16LittleEndian(packet[10..], header.StreamId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], header.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[16..], header.FrameLength);
    }

    /// <summary>Parses and sanity-checks a shard datagram; false for anything else or anything impossible.</summary>
    public static bool TryParse(ReadOnlySpan<byte> packet, out VideoShardHeader header)
    {
        header = default;
        if (packet.Length != Size || !Wire.HasHeader(packet, Wire.TypeVideoShard))
            return false;

        var parsed = new VideoShardHeader(
            StreamId: BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]),
            Frame: BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]),
            Keyframe: (packet[4] & FlagKeyframe) != 0,
            FrameLength: BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]),
            Block: packet[5],
            BlockCount: packet[6],
            Shard: packet[7],
            DataShards: packet[8],
            ParityShards: packet[9],
            Paused: (packet[4] & FlagPaused) != 0);

        if (parsed.BlockCount == 0
            || parsed.Block >= parsed.BlockCount
            || parsed.DataShards == 0
            || parsed.TotalShards > ReedSolomon.MaxTotalShards
            || parsed.Shard >= parsed.TotalShards
            || parsed.FrameLength == 0
            || parsed.FrameLength > MaxFrameLength)
            return false;

        // Only shapes the packetizer can produce, so forged headers can't make a client hold
        // more than a few real frames' worth of memory.
        int dataShards = DataShardsFor(parsed.FrameLength);
        if (parsed.BlockCount != BlocksFor(dataShards)
            || parsed.DataShards != DataShardsInBlock(dataShards, parsed.BlockCount, parsed.Block)
            || parsed.ParityShards < ParityShardsFor(parsed.DataShards, MinParityPercent)
            || parsed.ParityShards > ParityShardsFor(parsed.DataShards, MaxParityPercent))
            return false;

        header = parsed;
        return true;
    }

    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet) => packet.Slice(HeaderSize, PayloadSize);
}
