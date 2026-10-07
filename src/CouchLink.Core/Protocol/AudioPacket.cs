using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> client video port: one 5 ms Opus frame, plus a copy of the frame before it so a client
/// can rebuild a single lost packet exactly. After the 4-byte header: stream ID (u16), sequence
/// (u32), frame length (u16), frame, previous length (u16, 0 = none), previous frame.
/// <see cref="Frame"/> and <see cref="Previous"/> from <see cref="TryParse"/> point into the
/// datagram; they are not copies.
/// </summary>
public readonly record struct AudioPacket(
    ushort StreamId, uint Sequence, ReadOnlyMemory<byte> Frame, ReadOnlyMemory<byte> Previous)
{
    public const int HeaderSize = 14; // header 4, stream 2, sequence 4, two lengths 2 + 2
    public const int MaxFrameBytes = 400;

    public int Size => HeaderSize + Frame.Length + Previous.Length;

    public byte[] ToArray()
    {
        if (Frame.Length is 0 or > MaxFrameBytes)
            throw new ArgumentException($"A frame must be 1-{MaxFrameBytes} bytes.");
        if (Previous.Length > MaxFrameBytes)
            throw new ArgumentException($"The previous frame must be at most {MaxFrameBytes} bytes.");

        var packet = new byte[Size];
        var span = packet.AsSpan();
        Wire.WriteHeader(span, Wire.TypeAudio);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], StreamId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[6..], Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)Frame.Length);
        Frame.Span.CopyTo(span[12..]);
        int at = 12 + Frame.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)Previous.Length);
        Previous.Span.CopyTo(span[(at + 2)..]);
        return packet;
    }

    public static bool TryParse(byte[] datagram, out AudioPacket packet)
    {
        packet = default;
        var span = datagram.AsSpan();
        if (span.Length < HeaderSize || !Wire.HasHeader(span, Wire.TypeAudio))
            return false;
        int frameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
        if (frameLength is 0 or > MaxFrameBytes || span.Length < HeaderSize + frameLength)
            return false;
        int at = 12 + frameLength;
        int previousLength = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
        if (previousLength > MaxFrameBytes || span.Length != HeaderSize + frameLength + previousLength)
            return false;

        packet = new AudioPacket(
            BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[6..]),
            datagram.AsMemory(12, frameLength),
            datagram.AsMemory(at + 2, previousLength));
        return true;
    }
}
