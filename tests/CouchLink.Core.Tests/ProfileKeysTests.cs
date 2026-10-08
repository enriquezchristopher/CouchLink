using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ProfileKeysTests
{
    [Fact]
    public void Every_code_round_trips_through_its_id()
    {
        for (int vk = 1; vk <= 0xFF; vk++)
        {
            Assert.True(ProfileKeys.TryParse(ProfileKeys.IdOf((ushort)vk), out ushort back), $"0x{vk:X2}");
            Assert.Equal((ushort)vk, back);
        }
    }

    [Fact]
    public void Every_default_key_has_a_named_id()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            foreach (var key in layout.KeysFor(control))
                Assert.False(ProfileKeys.IdOf(key).StartsWith("0x", StringComparison.Ordinal), $"0x{key:X2}");
    }

    [Theory]
    [InlineData("J", 0x4A)]
    [InlineData("7", 0x37)]
    [InlineData("LeftClick", 0x01)]
    [InlineData("Mouse4", 0x05)]
    [InlineData("LShift", 0xA0)]
    [InlineData("RCtrl", 0xA3)]
    [InlineData("LAlt", 0xA4)]
    [InlineData("Space", 0x20)]
    [InlineData("Up", 0x26)]
    [InlineData("F3", 0x72)]
    [InlineData("F24", 0x87)]
    [InlineData("Num0", 0x60)]
    [InlineData("NumDivide", 0x6F)]
    [InlineData("Semicolon", 0xBA)]
    [InlineData("Quote", 0xDE)]
    [InlineData("0xE2", 0xE2)]
    [InlineData("0x4a", 0x4A)]
    public void Ids_map_to_their_codes(string id, int vk)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort parsed));
        Assert.Equal((ushort)vk, parsed);
    }

    [Theory]
    [InlineData("lshift", 0xA0)]
    [InlineData("LEFTCLICK", 0x01)]
    [InlineData("j", 0x4A)]
    public void Matching_ignores_case(string id, int vk)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort parsed));
        Assert.Equal((ushort)vk, parsed);
    }

    [Theory]
    [InlineData("Spcae")]
    [InlineData("")]
    [InlineData("0x00")]
    [InlineData("0x100")]
    [InlineData("0xZZ")]
    [InlineData("0x")]
    [InlineData("Left click")]
    public void Unknown_ids_are_refused(string id) => Assert.False(ProfileKeys.TryParse(id, out _));

    [Theory]
    [InlineData("Esc")]
    [InlineData("F1")]
    [InlineData("F2")]
    [InlineData("LWin")]
    [InlineData("RWin")]
    public void Reserved_ids_parse_to_reserved_codes(string id)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort vk));
        Assert.True(KeyLayout.IsReserved(vk));
    }

    [Fact]
    public void Codes_without_an_id_are_written_as_hex() => Assert.Equal("0xE2", ProfileKeys.IdOf(0xE2));
}
