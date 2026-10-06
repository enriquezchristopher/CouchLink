using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Client -> host input port: "echo this back" (16 bytes: header, slot, 3 reserved zero bytes,
/// the client's clock in ticks). The reply's arrival time gives the network round trip.
/// </summary>
public readonly record struct TimingPing(byte Slot, long ClientTicks)
{
    public const int Size = 16;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeTimingPing);
        destination[4] = Slot;
        destination[5..8].Clear();
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], ClientTicks);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out TimingPing ping)
    {
        ping = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeTimingPing))
            return false;
        ping = new TimingPing(source[4], BinaryPrimitives.ReadInt64LittleEndian(source[8..]));
        return true;
    }
}
