using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class InputPacketTests
{
    private static readonly InputPacket Sample = new(
        Slot: 3, Epoch: 0xA1B2C3D4, Sequence: 42,
        State: new PadState(PadButtons.Cross | PadButtons.DpadLeft, 1, 2, 3, 4, 5, 6));

    private static byte[] Bytes(InputPacket p)
    {
        var b = new byte[InputPacket.Size];
        p.WriteTo(b);
        return b;
    }

    [Fact]
    public void Round_trips()
    {
        Assert.True(InputPacket.TryParse(Bytes(Sample), out var parsed));
        Assert.Equal(Sample, parsed);
    }

    [Fact]
    public void Header_layout_is_fixed()
    {
        var b = Bytes(Sample);
        Assert.Equal(new byte[] { 0x43, 0x4C, 1, 1, 3, 0 }, b[..6]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, b[18..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    public void Wrong_length_is_rejected(int length)
    {
        Assert.False(InputPacket.TryParse(new byte[length], out _));
    }

    [Theory]
    [InlineData(0)] // magic low byte
    [InlineData(1)] // magic high byte
    [InlineData(2)] // version
    [InlineData(3)] // type
    public void Wrong_header_is_rejected(int index)
    {
        var b = Bytes(Sample);
        b[index] ^= 0xFF;
        Assert.False(InputPacket.TryParse(b, out _));
    }

    [Fact]
    public void Random_garbage_is_rejected()
    {
        var b = new byte[InputPacket.Size];
        new Random(1234).NextBytes(b);
        Assert.False(InputPacket.TryParse(b, out _));
    }

    [Fact]
    public void Unknown_button_bits_are_stripped()
    {
        var b = Bytes(Sample);
        b[17] = 0xFF; // top byte of buttons: bits 24..31 are undefined
        Assert.True(InputPacket.TryParse(b, out var parsed));
        Assert.Equal(PadButtons.Cross | PadButtons.DpadLeft, parsed.State.Buttons);
    }

    [Fact]
    public void WriteTo_rejects_short_buffer()
    {
        Assert.Throws<ArgumentException>(() => Sample.WriteTo(new byte[10]));
    }
}
