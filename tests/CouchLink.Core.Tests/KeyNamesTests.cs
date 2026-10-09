using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x37, "7")]
    [InlineData(0x01, "Left click")]
    [InlineData(0x05, "Mouse 4")]
    [InlineData(0x06, "Mouse 5")]
    [InlineData(0xA2, "Left Ctrl")]
    [InlineData(0xA5, "Right Alt")]
    [InlineData(0x26, "↑")]
    [InlineData(0x20, "Space")]
    [InlineData(0x74, "F5")]
    [InlineData(0x63, "Num 3")]
    [InlineData(0xBC, ",")]
    public void Keys_have_readable_names(ushort vk, string name)
    {
        Assert.Equal(name, KeyNames.Of(vk));
    }

    [Fact]
    public void An_unknown_key_falls_back_to_its_code()
    {
        Assert.Equal("Key 0xFF", KeyNames.Of((ushort)0xFF));
    }

    [Fact]
    public void Every_default_key_has_a_real_name()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            foreach (var key in layout.KeysFor(control))
                Assert.DoesNotContain("Key 0x", KeyNames.Of(key));
    }

    [Fact]
    public void Every_control_has_a_name_and_one_group()
    {
        foreach (var control in Enum.GetValues<PadControl>())
        {
            Assert.False(string.IsNullOrWhiteSpace(KeyNames.Of(control)));
            Assert.Single(KeyNames.Groups, g => g.Controls.Contains(control));
        }
    }

    [Fact]
    public void Describe_joins_keys()
    {
        Assert.Equal("J / Left click", KeyNames.Describe(KeyLayout.CreateDefault(), PadControl.Square));
    }

    [Fact]
    public void Describe_shows_none_for_a_control_without_keys()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.Circle, VirtualKeys.Letter('K')); // Cross loses its only key
        Assert.Equal("(none)", KeyNames.Describe(layout, PadControl.Cross));
    }

    [Fact]
    public void A_label_goes_before_the_control_name()
    {
        Assert.Equal("Shoot (Square)", KeyNames.Labelled(PadControl.Square, "Shoot"));
        Assert.Equal("D-pad up", KeyNames.Labelled(PadControl.DpadUp, null));
    }

    [Theory]
    [InlineData(PadControl.RightUp, "Right stick up")]
    [InlineData(PadControl.RightDown, "Right stick down")]
    [InlineData(PadControl.RightLeft, "Right stick left")]
    [InlineData(PadControl.RightRight, "Right stick right")]
    public void Right_stick_directions_have_names(PadControl control, string name)
    {
        Assert.Equal(name, KeyNames.Of(control));
    }

    [Fact]
    public void The_right_stick_group_follows_the_left_stick()
    {
        Assert.Equal(["Left stick", "Right stick", "D-pad", "Buttons", "Shoulders", "Stick clicks", "Menu"],
            KeyNames.Groups.Select(g => g.Name));
        Assert.Same(KeyNames.RightStick, KeyNames.Groups[1].Controls);
        Assert.Equal([PadControl.RightUp, PadControl.RightDown, PadControl.RightLeft, PadControl.RightRight], KeyNames.RightStick);
    }

    [Fact]
    public void The_default_layout_has_no_right_stick_keys()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in KeyNames.RightStick)
            Assert.Empty(layout.KeysFor(control));
    }
}
