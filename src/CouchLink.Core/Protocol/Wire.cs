using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>The first four bytes of every CouchLink datagram: magic "CL", version, packet type.</summary>
public static class Wire
{
    public const ushort Magic = 0x4C43; // "CL"
    public const byte Version = 1;

    public const byte TypeInput = 1;
    public const byte TypeVideoShard = 2;
    public const byte TypeKeyframeRequest = 3;
    public const byte TypeTimingPing = 4;
    public const byte TypeTimingReply = 5;

    public static void WriteHeader(Span<byte> destination, byte type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
        destination[2] = Version;
        destination[3] = type;
    }

    public static bool HasHeader(ReadOnlySpan<byte> source, byte type) =>
        source.Length >= 4
        && BinaryPrimitives.ReadUInt16LittleEndian(source) == Magic
        && source[2] == Version
        && source[3] == type;
}
