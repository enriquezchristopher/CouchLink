using System.Text;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class PcNameTests
{
    [Fact]
    public void A_short_name_is_kept()
    {
        Assert.Equal("PC-07", PcName.Clip("PC-07"));
    }

    [Fact]
    public void A_long_name_is_cut_to_63_bytes()
    {
        Assert.Equal(new string('A', 63), PcName.Clip(new string('A', 80)));
    }

    [Fact]
    public void A_cut_never_splits_a_character()
    {
        var clipped = PcName.Clip(new string('é', 40)); // 2 bytes each
        Assert.Equal(31, clipped.Length);
        Assert.Equal(62, Encoding.UTF8.GetByteCount(clipped));

        var emoji = PcName.Clip(new string('A', 61) + "😀"); // 4-byte character does not fit
        Assert.Equal(new string('A', 61), emoji);
    }

    [Fact]
    public void A_blank_name_becomes_a_placeholder()
    {
        Assert.Equal("Unknown PC", PcName.Clip("   "));
    }

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        Assert.True(PcName.TryDecode(PcName.Encode("Café-03"), out var name));
        Assert.Equal("Café-03", name);
    }

    [Fact]
    public void Decode_rejects_empty_too_long_and_invalid_utf8()
    {
        Assert.False(PcName.TryDecode([], out _));
        Assert.False(PcName.TryDecode(new byte[64], out _));
        Assert.False(PcName.TryDecode([0xC3], out _)); // half a character
    }
}
