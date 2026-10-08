using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> LAN, once a second on UDP 47800 (type 7). After the 4-byte header: players (1),
/// capacity (1), name length (1), name. <see cref="TryParse"/> accepts any version, so a host on
/// another version is still listed (greyed out) instead of silently missing.
/// </summary>
public readonly record struct HostAnnounce(byte Version, byte Players, byte Capacity, string Name)
{
    private const int FixedSize = 7;

    public bool Compatible => Version == Wire.Version;

    public static HostAnnounce For(int players, int capacity, string name) =>
        new(Wire.Version, (byte)players, (byte)capacity, PcName.Clip(name));

    public byte[] ToArray()
    {
        var name = PcName.Encode(Name);
        var bytes = new byte[FixedSize + name.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, Wire.Magic);
        bytes[2] = Version;
        bytes[3] = Wire.TypeHostAnnounce;
        bytes[4] = Players;
        bytes[5] = Capacity;
        bytes[6] = (byte)name.Length;
        name.CopyTo(bytes, FixedSize);
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out HostAnnounce announce)
    {
        announce = default;
        if (source.Length < FixedSize
            || BinaryPrimitives.ReadUInt16LittleEndian(source) != Wire.Magic
            || source[3] != Wire.TypeHostAnnounce)
            return false;

        byte players = source[4], capacity = source[5];
        int nameLength = source[6];
        if (capacity == 0 || players > capacity || source.Length != FixedSize + nameLength)
            return false;
        if (!PcName.TryDecode(source.Slice(FixedSize, nameLength), out var name))
            return false;

        announce = new HostAnnounce(source[2], players, capacity, name);
        return true;
    }
}
