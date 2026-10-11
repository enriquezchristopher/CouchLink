using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

public class PadKindTests
{
    [Fact]
    public void Kinds_keep_their_wire_values()
    {
        Assert.Equal(1, (byte)PadKind.DualShock4);
        Assert.Equal(2, (byte)PadKind.Xbox360);
    }

    [Theory]
    [InlineData(PadKind.DualShock4, "DualShock 4", 9)]
    [InlineData(PadKind.Xbox360, "Xbox 360", 4)]
    public void Each_kind_has_a_label_and_a_capacity(PadKind kind, string label, int capacity)
    {
        Assert.Equal(label, PadKinds.Label(kind));
        Assert.Equal(capacity, PadKinds.Capacity(kind));
    }
}
