using System.Buffers.Binary;
using CouchLink.Core.Input;

namespace CouchLink.Core.Protocol;

/// <summary>One client -> host controller-state datagram (24 bytes, little-endian).</summary>
public readonly record struct InputPacket(byte Slot, uint Epoch, uint Sequence, PadState State)
{
    public const int Size = 24;
    private const ushort Magic = 0x4C43; // "CL"
    private const byte Version = 1;
    private const byte TypeInput = 1;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
        destination[2] = Version;
        destination[3] = TypeInput;
        destination[4] = Slot;
        destination[5] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], Epoch);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[14..], (uint)State.Buttons);
        destination[18] = State.LX;
        destination[19] = State.LY;
        destination[20] = State.RX;
        destination[21] = State.RY;
        destination[22] = State.L2;
        destination[23] = State.R2;
    }

    /// <summary>Parses a datagram; returns false for anything that is not a v1 input packet.</summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out InputPacket packet)
    {
        packet = default;
        if (source.Length != Size
            || BinaryPrimitives.ReadUInt16LittleEndian(source) != Magic
            || source[2] != Version
            || source[3] != TypeInput)
            return false;

        var buttons = (PadButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[14..]) & PadButtons.Known;
        var state = new PadState(buttons, source[18], source[19], source[20], source[21], source[22], source[23]);
        packet = new InputPacket(
            source[4],
            BinaryPrimitives.ReadUInt32LittleEndian(source[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[10..]),
            state);
        return true;
    }
}
