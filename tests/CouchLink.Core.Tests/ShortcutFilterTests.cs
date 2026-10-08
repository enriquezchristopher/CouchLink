using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ShortcutFilterTests
{
    [Theory]
    [InlineData(VirtualKeys.LWin, false, false)]
    [InlineData(VirtualKeys.RWin, false, false)]
    [InlineData(VirtualKeys.LWin, true, true)]
    [InlineData(VirtualKeys.Tab, true, false)]     // Alt+Tab
    [InlineData(VirtualKeys.Escape, true, false)]  // Alt+Esc
    [InlineData(VirtualKeys.Escape, false, true)]  // Ctrl+Esc
    public void Windows_shortcuts_are_blocked(ushort vk, bool alt, bool ctrl)
    {
        Assert.True(ShortcutFilter.ShouldBlock(vk, alt, ctrl));
    }

    [Theory]
    [InlineData(VirtualKeys.Tab, false, false)]        // Touchpad by default
    [InlineData(VirtualKeys.Escape, false, false)]
    [InlineData(VirtualKeys.LMenu, true, false)]       // Alt alone
    [InlineData((ushort)0x73, true, false)]            // Alt+F4 still leaves
    [InlineData((ushort)0x51, true, true)]             // Ctrl+Alt+Q
    [InlineData((ushort)0x43, true, true)]             // Ctrl+Alt+C
    [InlineData((ushort)0x4B, false, false)]           // K
    public void Everything_else_passes(ushort vk, bool alt, bool ctrl)
    {
        Assert.False(ShortcutFilter.ShouldBlock(vk, alt, ctrl));
    }
}
