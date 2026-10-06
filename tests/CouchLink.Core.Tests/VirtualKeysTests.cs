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
}
