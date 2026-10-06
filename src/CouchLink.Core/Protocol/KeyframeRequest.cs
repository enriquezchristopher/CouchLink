namespace CouchLink.Core.Protocol;

/// <summary>
/// Client -> host: "send a keyframe" (8 bytes: header, slot, 3 reserved zero bytes). Sent to the
/// host's input port until the v1.4 session channel exists.
/// </summary>
public readonly record struct KeyframeRequest(byte Slot)
{
    public const int Size = 8;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeKeyframeRequest);
        destination[4] = Slot;
        destination[5..Size].Clear();
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out KeyframeRequest request)
    {
        request = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeKeyframeRequest))
            return false;
        request = new KeyframeRequest(source[4]);
        return true;
    }
}
