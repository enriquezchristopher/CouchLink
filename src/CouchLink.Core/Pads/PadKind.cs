namespace CouchLink.Core.Pads;

/// <summary>Which virtual controller the host gives every player. Sent on the wire: keep the values.</summary>
public enum PadKind : byte
{
    DualShock4 = 1,
    Xbox360 = 2,
}

public static class PadKinds
{
    public static string Label(PadKind kind) => kind == PadKind.Xbox360 ? "Xbox 360" : "DualShock 4";

    /// <summary>Clients the host takes (the host player is extra). Windows shows games at most 4 Xbox controllers.</summary>
    public static int Capacity(PadKind kind) => kind == PadKind.Xbox360 ? 4 : PadManager.LastSlot - PadManager.FirstSlot + 1;
}
