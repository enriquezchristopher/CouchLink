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
    byte ParityShards)
{
    public int TotalShards => DataShards + ParityShards;
}

/// <summary>Host -> client video shard datagram: 20-byte header + 1200-byte payload.</summary>
public static class VideoShardPacket
{
    public const int HeaderSize = 20;
    public const int PayloadSize = 1200;
    public const int Size = HeaderSize + PayloadSize;

    private const byte FlagKeyframe = 1;

    public static void WriteHeader(Span<byte> packet, in VideoShardHeader header)
    {
        if (packet.Length < HeaderSize)
            throw new ArgumentException($"Need {HeaderSize} bytes.", nameof(packet));

        Wire.WriteHeader(packet, Wire.TypeVideoShard);
        packet[4] = header.Keyframe ? FlagKeyframe : (byte)0;
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
            ParityShards: packet[9]);

        if (parsed.BlockCount == 0
            || parsed.Block >= parsed.BlockCount
            || parsed.DataShards == 0
            || parsed.TotalShards > ReedSolomon.MaxTotalShards
            || parsed.Shard >= parsed.TotalShards
            || parsed.FrameLength == 0)
            return false;

        header = parsed;
        return true;
    }

    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet) => packet.Slice(HeaderSize, PayloadSize);
}
