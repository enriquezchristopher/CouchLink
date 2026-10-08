using System.Globalization;

namespace CouchLink.Core.Input;

/// <summary>
/// The key IDs profile files use ("LShift", "LeftClick"). Fixed for good, unlike the display names in
/// <see cref="KeyNames"/>. A code with no ID is written as "0xNN". Matching ignores case. The reserved
/// keys have IDs too, so a file naming one gets "F1 is reserved" rather than "unknown key".
/// </summary>
public static class ProfileKeys
{
    private static readonly Dictionary<ushort, string> Ids = BuildIds();
    private static readonly Dictionary<string, ushort> Codes =
        Ids.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static string IdOf(ushort vk) => Ids.TryGetValue(vk, out var id) ? id : $"0x{vk:X2}";

    public static bool TryParse(string id, out ushort vk)
    {
        if (Codes.TryGetValue(id, out vk))
            return true;
        if (id.Length is 3 or 4 && id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(id.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out vk)
            && vk is >= 1 and <= 0xFF)
            return true;
        vk = 0;
        return false;
    }

    private static Dictionary<ushort, string> BuildIds()
    {
        var ids = new Dictionary<ushort, string>
        {
            [VirtualKeys.LButton] = "LeftClick", [VirtualKeys.RButton] = "RightClick", [VirtualKeys.MButton] = "MiddleClick",
            [VirtualKeys.XButton1] = "Mouse4", [VirtualKeys.XButton2] = "Mouse5",
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
            [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "LWin", [0x5C] = "RWin", [0x5D] = "Menu",
            [0x6A] = "NumMultiply", [0x6B] = "NumAdd", [0x6D] = "NumSubtract", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
            [0x90] = "NumLock", [0x91] = "ScrollLock",
            [0xA0] = "LShift", [0xA1] = "RShift", [0xA2] = "LCtrl", [0xA3] = "RCtrl", [0xA4] = "LAlt", [0xA5] = "RAlt",
            [0xBA] = "Semicolon", [0xBB] = "Equals", [0xBC] = "Comma", [0xBD] = "Minus", [0xBE] = "Period",
            [0xBF] = "Slash", [0xC0] = "Backquote",
            [0xDB] = "LeftBracket", [0xDC] = "Backslash", [0xDD] = "RightBracket", [0xDE] = "Quote",
        };
        for (char c = 'A'; c <= 'Z'; c++)
            ids[c] = c.ToString();
        for (char c = '0'; c <= '9'; c++)
            ids[c] = c.ToString();
        for (int n = 1; n <= 24; n++)
            ids[(ushort)(0x6F + n)] = $"F{n}";
        for (int n = 0; n <= 9; n++)
            ids[(ushort)(0x60 + n)] = $"Num{n}";
        return ids;
    }
}
