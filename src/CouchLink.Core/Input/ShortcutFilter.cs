namespace CouchLink.Core.Input;

/// <summary>
/// The Windows shortcuts the player's keyboard hook swallows while the game is in front (spec 6.4):
/// both Windows keys (so every Win+ combination), Alt+Tab, Alt+Esc and Ctrl+Esc. Alt+F4 and
/// Ctrl+Alt+Q pass so there is always a way out.
/// </summary>
public static class ShortcutFilter
{
    public static bool ShouldBlock(ushort vk, bool altDown, bool ctrlDown) => vk switch
    {
        VirtualKeys.LWin or VirtualKeys.RWin => true,
        VirtualKeys.Tab => altDown,
        VirtualKeys.Escape => altDown || ctrlDown,
        _ => false,
    };
}
