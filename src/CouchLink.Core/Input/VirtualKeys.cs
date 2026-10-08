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
    public static ushort FromRawKeyboard(ushort vkey, ushort makeCode, ushort flags) => vkey switch
    {
        Shift => makeCode == RightShiftMakeCode ? RShift : LShift,
        Control => (flags & ExtendedKeyFlag) != 0 ? RControl : LControl,
        Menu => (flags & ExtendedKeyFlag) != 0 ? RMenu : LMenu,
        _ => vkey,
    };
}
