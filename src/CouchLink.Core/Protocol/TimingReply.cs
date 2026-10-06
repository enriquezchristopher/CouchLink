using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> client video port: the ping's client ticks echoed back, plus the host's recent
/// capture-to-send delay (16 bytes: header, delay in microseconds, the echoed ticks).
/// </summary>
public readonly record struct TimingReply(long ClientTicks, TimeSpan HostDelay)
{
    public const int Size = 16;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeTimingReply);
        double micros = Math.Clamp(HostDelay.TotalMicroseconds, 0, uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)micros);
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], ClientTicks);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out TimingReply reply)
    {
        reply = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeTimingReply))
            return false;
        reply = new TimingReply(
            BinaryPrimitives.ReadInt64LittleEndian(source[8..]),
            TimeSpan.FromMicroseconds(BinaryPrimitives.ReadUInt32LittleEndian(source[4..])));
        return true;
    }
}
