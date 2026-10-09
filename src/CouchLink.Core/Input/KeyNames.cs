namespace CouchLink.Core.Input;

/// <summary>What the controls editor and the F1 panel call keys and controls. A table, no Windows API.</summary>
public static class KeyNames
{
    private static readonly Dictionary<ushort, string> Named = new()
    {
        [0x01] = "Left click", [0x02] = "Right click", [0x04] = "Middle click", [0x05] = "Mouse 4", [0x06] = "Mouse 5",
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc", [0x20] = "Space",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Left Windows", [0x5C] = "Right Windows", [0x5D] = "Menu",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift", [0xA1] = "Right Shift", [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt", [0xA5] = "Right Alt",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    /// <summary>The right stick's four directions. They have no keys by default: the mouse drives the stick.</summary>
    public static PadControl[] RightStick { get; } =
        [PadControl.RightUp, PadControl.RightDown, PadControl.RightLeft, PadControl.RightRight];

    /// <summary>The controls in the order the editor and the F1 panel list them (spec 6.1).</summary>
    public static IReadOnlyList<(string Name, PadControl[] Controls)> Groups { get; } =
    [
        ("Left stick", [PadControl.LeftUp, PadControl.LeftDown, PadControl.LeftLeft, PadControl.LeftRight]),
        ("Right stick", RightStick),
        ("D-pad", [PadControl.DpadUp, PadControl.DpadDown, PadControl.DpadLeft, PadControl.DpadRight]),
        ("Buttons", [PadControl.Cross, PadControl.Circle, PadControl.Square, PadControl.Triangle]),
        ("Shoulders", [PadControl.L1, PadControl.R1, PadControl.L2, PadControl.R2]),
        ("Stick clicks", [PadControl.L3, PadControl.R3]),
        ("Menu", [PadControl.Options, PadControl.Share, PadControl.Touchpad]),
    ];

    public static string Of(ushort vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x6F}";
        if (vk is >= 0x60 and <= 0x69)
            return $"Num {vk - 0x60}";
        return Named.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";
    }

    public static string Of(PadControl control) => control switch
    {
        PadControl.LeftUp => "Left stick up",
        PadControl.LeftDown => "Left stick down",
        PadControl.LeftLeft => "Left stick left",
        PadControl.LeftRight => "Left stick right",
        PadControl.RightUp => "Right stick up",
        PadControl.RightDown => "Right stick down",
        PadControl.RightLeft => "Right stick left",
        PadControl.RightRight => "Right stick right",
        PadControl.DpadUp => "D-pad up",
        PadControl.DpadDown => "D-pad down",
        PadControl.DpadLeft => "D-pad left",
        PadControl.DpadRight => "D-pad right",
        _ => control.ToString(), // Cross, Circle, L1, Options, Touchpad...
    };

    /// <summary>A control as the player sees it: "Shoot (Square)" when a profile names it, else "Square".</summary>
    public static string Labelled(PadControl control, string? label) =>
        label is null ? Of(control) : $"{label} ({Of(control)})";

    /// <summary>The control's keys, e.g. "J / Left click", or "(none)".</summary>
    public static string Describe(KeyLayout layout, PadControl control)
    {
        var keys = layout.KeysFor(control);
        return keys.Count == 0 ? "(none)" : string.Join(" / ", keys.Select(Of));
    }
}
