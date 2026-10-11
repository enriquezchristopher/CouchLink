using CouchLink.Core.Input;
using CouchLink.Core.Pads;

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

    [Theory]
    [InlineData(PadControl.Cross, "A")]
    [InlineData(PadControl.Circle, "B")]
    [InlineData(PadControl.Square, "X")]
    [InlineData(PadControl.Triangle, "Y")]
    [InlineData(PadControl.L1, "LB")]
    [InlineData(PadControl.R1, "RB")]
    [InlineData(PadControl.L2, "LT")]
    [InlineData(PadControl.R2, "RT")]
    [InlineData(PadControl.L3, "LS")]
    [InlineData(PadControl.R3, "RS")]
    [InlineData(PadControl.Options, "Start")]
    [InlineData(PadControl.Share, "Back")]
    [InlineData(PadControl.LeftUp, "Left stick up")]
    [InlineData(PadControl.DpadLeft, "D-pad left")]
    public void Xbox_controls_have_Xbox_names(PadControl control, string name)
    {
        Assert.Equal(name, KeyNames.Of(control, PadKind.Xbox360));
    }

    [Fact]
    public void DualShock_names_are_unchanged()
    {
        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Equal(KeyNames.Of(control), KeyNames.Of(control, PadKind.DualShock4));
    }

    [Fact]
    public void Xbox_groups_have_no_touchpad_and_keep_the_right_stick_instance()
    {
        var xbox = KeyNames.GroupsFor(PadKind.Xbox360);
        Assert.Equal(KeyNames.Groups.Select(g => g.Name), xbox.Select(g => g.Name));
        Assert.Equal([PadControl.Options, PadControl.Share], xbox.Single(g => g.Name == "Menu").Controls);
        Assert.Same(KeyNames.RightStick, xbox.Single(g => g.Name == "Right stick").Controls);
        Assert.Same(KeyNames.Groups, KeyNames.GroupsFor(PadKind.DualShock4));
    }

    [Fact]
    public void Labels_use_the_kind()
    {
        Assert.Equal("Pass (A)", KeyNames.Labelled(PadControl.Cross, "Pass", PadKind.Xbox360));
        Assert.Equal("Pass (Cross)", KeyNames.Labelled(PadControl.Cross, "Pass"));
    }
}
