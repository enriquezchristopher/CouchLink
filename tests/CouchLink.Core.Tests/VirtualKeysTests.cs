using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class VirtualKeysTests
{
    private const ushort KeyDown = 0x00;
    private const ushort KeyUp = 0x01; // RI_KEY_BREAK
    private const ushort Extended = 0x02; // RI_KEY_E0

    [Theory]
    [InlineData(0x10, 0x2A, KeyDown, VirtualKeys.LShift)]
    [InlineData(0x10, 0x36, KeyDown, VirtualKeys.RShift)]
    [InlineData(0x10, 0x36, KeyUp, VirtualKeys.RShift)]
    [InlineData(0x11, 0x1D, KeyDown, VirtualKeys.LControl)]
    [InlineData(0x11, 0x1D, Extended, VirtualKeys.RControl)]
    [InlineData(0x11, 0x1D, Extended | KeyUp, VirtualKeys.RControl)]
    public void Raw_Shift_and_Ctrl_become_left_or_right_keys(ushort vkey, ushort makeCode, ushort flags, ushort expected)
    {
        Assert.Equal(expected, VirtualKeys.FromRawKeyboard(vkey, makeCode, flags));
    }

    [Theory]
    [InlineData(0x41, 0x1E, KeyDown)] // A
    [InlineData(0x26, 0x48, Extended)] // Up arrow
    [InlineData(0x0D, 0x1C, KeyDown)] // Enter
    public void Other_keys_keep_their_code(ushort vkey, ushort makeCode, ushort flags)
    {
        Assert.Equal(vkey, VirtualKeys.FromRawKeyboard(vkey, makeCode, flags));
    }

    // Num Lock on and Shift held: Windows turns the numpad into navigation keys (VK_UP for Num8, VK_CLEAR for
    // Num5), but they keep the numpad's make code and have no E0 flag, unlike the real arrow keys.
    [Theory]
    [InlineData(0x24, 0x47, 0x67)] // Num7 -> Home
    [InlineData(0x26, 0x48, 0x68)] // Num8 -> Up
    [InlineData(0x21, 0x49, 0x69)] // Num9 -> PageUp
    [InlineData(0x25, 0x4B, 0x64)] // Num4 -> Left
    [InlineData(0x0C, 0x4C, 0x65)] // Num5 -> Clear
    [InlineData(0x27, 0x4D, 0x66)] // Num6 -> Right
    [InlineData(0x23, 0x4F, 0x61)] // Num1 -> End
    [InlineData(0x28, 0x50, 0x62)] // Num2 -> Down
    [InlineData(0x22, 0x51, 0x63)] // Num3 -> PageDown
    [InlineData(0x2D, 0x52, 0x60)] // Num0 -> Insert
    [InlineData(0x2E, 0x53, 0x6E)] // NumDecimal -> Delete
    public void A_numpad_key_turned_into_a_navigation_key_by_Shift_is_the_numpad_key_again(ushort vkey, ushort makeCode, ushort expected)
    {
        Assert.Equal(expected, VirtualKeys.FromRawKeyboard(vkey, makeCode, KeyDown, numLockOn: true));
        Assert.Equal(expected, VirtualKeys.FromRawKeyboard(vkey, makeCode, KeyUp, numLockOn: true));
    }

    [Theory]
    [InlineData(0x26, 0x48, Extended, true)] // the real Up arrow has E0
    [InlineData(0x25, 0x4B, Extended | KeyUp, true)]
    [InlineData(0x26, 0x48, KeyDown, false)] // Num Lock off: the numpad really is navigation
    [InlineData(0x41, 0x1E, KeyDown, true)] // a letter
    public void Real_navigation_keys_and_Num_Lock_off_are_left_alone(ushort vkey, ushort makeCode, ushort flags, bool numLockOn)
    {
        Assert.Equal(vkey, VirtualKeys.FromRawKeyboard(vkey, makeCode, flags, numLockOn));
    }

    [Theory]
    [InlineData(0x10, 0x2A, Extended, true)] // the fake Shift
    [InlineData(0x10, 0x2A, Extended | KeyUp, true)]
    [InlineData(0x10, 0x2A, KeyDown, false)] // the real left Shift has no E0
    [InlineData(0x10, 0x36, KeyDown, false)] // right Shift
    [InlineData(0x11, 0x1D, Extended, false)] // right Ctrl is E0 too
    public void Only_the_fake_Shift_is_recognised(ushort vkey, ushort makeCode, ushort flags, bool expected)
    {
        Assert.Equal(expected, VirtualKeys.IsFakeShift(vkey, makeCode, flags));
    }

    [Fact]
    public void A_Num_key_is_held_while_Windows_reports_its_navigation_key_down()
    {
        var down = new HashSet<ushort> { 0x26 }; // Shift+Num8 reports VK_UP
        Assert.True(VirtualKeys.IsHeld(0x68, down.Contains));
        Assert.False(VirtualKeys.IsHeld(0x69, down.Contains));
    }

    [Fact]
    public void A_key_is_held_when_it_is_down_itself()
    {
        var down = new HashSet<ushort> { 0x68, 0x41 };
        Assert.True(VirtualKeys.IsHeld(0x68, down.Contains));
        Assert.True(VirtualKeys.IsHeld(0x41, down.Contains));
        Assert.False(VirtualKeys.IsHeld(0x42, down.Contains));
    }

    [Fact]
    public void The_Up_arrow_is_not_held_just_because_Num8_is()
    {
        var down = new HashSet<ushort> { 0x68 };
        Assert.False(VirtualKeys.IsHeld(0x26, down.Contains));
    }

    [Theory]
    [InlineData(0x12, 0x38, KeyDown, VirtualKeys.LMenu)]
    [InlineData(0x12, 0x38, Extended, VirtualKeys.RMenu)]
    [InlineData(0x12, 0x38, Extended | KeyUp, VirtualKeys.RMenu)]
    public void Raw_Alt_becomes_left_or_right_alt(ushort vkey, ushort makeCode, ushort flags, ushort expected)
    {
        // WPF (the controls editor) reports 0xA4/0xA5; Raw Input (the game) reports 0x12.
        Assert.Equal(expected, VirtualKeys.FromRawKeyboard(vkey, makeCode, flags));
    }
}
