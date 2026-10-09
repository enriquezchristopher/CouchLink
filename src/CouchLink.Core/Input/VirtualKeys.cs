namespace CouchLink.Core.Input;

/// <summary>Win32 virtual-key codes. Mouse buttons use their VK codes too.</summary>
public static class VirtualKeys
{
    public const ushort LButton = 0x01;
    public const ushort RButton = 0x02;
    public const ushort MButton = 0x04;
    public const ushort XButton1 = 0x05;
    public const ushort XButton2 = 0x06;
    public const ushort Back = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Return = 0x0D;
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort LWin = 0x5B;
    public const ushort RWin = 0x5C;
    public const ushort F1 = 0x70;
    public const ushort F2 = 0x71;
    public const ushort LShift = 0xA0;
    public const ushort RShift = 0xA1;
    public const ushort LControl = 0xA2;
    public const ushort RControl = 0xA3;
    public const ushort LMenu = 0xA4;
    public const ushort RMenu = 0xA5;

    private const ushort Shift = 0x10;
    private const ushort Control = 0x11;
    private const ushort Menu = 0x12;
    private const ushort LeftShiftMakeCode = 0x2A;
    private const ushort RightShiftMakeCode = 0x36;
    private const ushort ExtendedKeyFlag = 0x02; // RI_KEY_E0

    /// <summary>VK code of a letter or digit key ('A'..'Z', '0'..'9').</summary>
    public static ushort Letter(char c) => char.ToUpperInvariant(c);

    /// <summary>
    /// Raw Input reports both Shift keys as 0x10, both Ctrl keys as 0x11 and both Alt keys as 0x12.
    /// Turns them into the left/right codes (by make code for Shift, E0 flag for Ctrl and Alt) so
    /// holding both and releasing one doesn't release the other, and so they match the codes WPF
    /// gives the controls editor. Other keys pass through.
    /// </summary>
    public static ushort FromRawKeyboard(ushort vkey, ushort makeCode, ushort flags, bool numLockOn = false) => vkey switch
    {
        Shift => makeCode == RightShiftMakeCode ? RShift : LShift,
        Control => (flags & ExtendedKeyFlag) != 0 ? RControl : LControl,
        Menu => (flags & ExtendedKeyFlag) != 0 ? RMenu : LMenu,
        _ when numLockOn && (flags & ExtendedKeyFlag) == 0 && NumpadKeyOf(vkey, makeCode) is var num and not 0 => num,
        _ => vkey,
    };

    /// <summary>The key code for one Raw Input keyboard event, or 0 when the event is not a real key and must be ignored.</summary>
    public static ushort FromRawEvent(ushort vkey, ushort makeCode, ushort flags, bool numLockOn) =>
        IsFakeShift(vkey, makeCode, flags) ? (ushort)0 : FromRawKeyboard(vkey, makeCode, flags, numLockOn);

    /// <summary>
    /// A Shift event Windows adds around a key, not a press of the Shift key. With Num Lock on and Shift held,
    /// a numpad key is wrapped in a Shift event (VK_SHIFT) that carries the numpad key's make code, and the real
    /// Shift never went up. The keyboard's own fake Shift (Shift's make code with the E0 flag) is the same thing.
    /// A Shift event with any other make code, such as 0 from a remote desktop, is real.
    /// </summary>
    public static bool IsFakeShift(ushort vkey, ushort makeCode, ushort flags)
    {
        if (vkey != Shift)
            return false;
        if (makeCode == LeftShiftMakeCode)
            return (flags & ExtendedKeyFlag) != 0;
        foreach (var (code, _, _) in ShiftedNumpad)
            if (code == makeCode)
                return true;
        return false;
    }

    /// <summary>
    /// Whether a key counts as physically down. With Num Lock on and Shift held, Windows reports a numpad key as
    /// its navigation key (Num8 as Up), so a Num key also counts when that key is down. Windows reports Shift
    /// itself as up while such a key is down, so Shift counts too.
    /// </summary>
    public static bool IsHeld(ushort vk, Func<ushort, bool> isPhysicallyDown)
    {
        if (isPhysicallyDown(vk))
            return true;
        foreach (var (_, nav, num) in ShiftedNumpad)
        {
            if (num == vk)
                return isPhysicallyDown(nav);
            if ((vk is LShift or RShift or Shift) && isPhysicallyDown(nav))
                return true;
        }
        return false;
    }

    // With Num Lock on and Shift held, a numpad key arrives as its navigation key with the numpad's make code and
    // no E0 flag (the real arrow and editing keys have E0).
    private static readonly (ushort MakeCode, ushort Nav, ushort Num)[] ShiftedNumpad =
    [
        (0x47, 0x24, 0x67), // Home      Num7
        (0x48, 0x26, 0x68), // Up        Num8
        (0x49, 0x21, 0x69), // PageUp    Num9
        (0x4B, 0x25, 0x64), // Left      Num4
        (0x4C, 0x0C, 0x65), // Clear     Num5
        (0x4D, 0x27, 0x66), // Right     Num6
        (0x4F, 0x23, 0x61), // End       Num1
        (0x50, 0x28, 0x62), // Down      Num2
        (0x51, 0x22, 0x63), // PageDown  Num3
        (0x52, 0x2D, 0x60), // Insert    Num0
        (0x53, 0x2E, 0x6E), // Delete    NumDecimal
    ];

    private static ushort NumpadKeyOf(ushort vkey, ushort makeCode)
    {
        foreach (var (code, nav, num) in ShiftedNumpad)
            if (code == makeCode && nav == vkey)
                return num;
        return 0;
    }
}
