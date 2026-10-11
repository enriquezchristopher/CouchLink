namespace CouchLink.Core.Input;

/// <summary>XInput's button bits, as ViGEmBus's Xbox 360 target takes them.</summary>
[Flags]
public enum Xbox360Buttons : ushort
{
    None = 0,
    Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
    Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200, Guide = 0x0400,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

/// <summary>
/// One Xbox 360 report from the client's DS4-shaped state. Sticks: DS4 bytes (128 center, Y down) become XInput
/// shorts (0 center, Y up), center exactly 0 and full tilt the full range. The touchpad has no Xbox button.
/// </summary>
public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons, byte LeftTrigger, byte RightTrigger, short LeftX, short LeftY, short RightX, short RightY)
{
    private static readonly (PadButtons From, Xbox360Buttons To)[] Map =
    [
        (PadButtons.Cross, Xbox360Buttons.A), (PadButtons.Circle, Xbox360Buttons.B),
        (PadButtons.Square, Xbox360Buttons.X), (PadButtons.Triangle, Xbox360Buttons.Y),
        (PadButtons.L1, Xbox360Buttons.LeftShoulder), (PadButtons.R1, Xbox360Buttons.RightShoulder),
        (PadButtons.L3, Xbox360Buttons.LeftThumb), (PadButtons.R3, Xbox360Buttons.RightThumb),
        (PadButtons.Options, Xbox360Buttons.Start), (PadButtons.Share, Xbox360Buttons.Back),
        (PadButtons.PS, Xbox360Buttons.Guide),
    ];

    public static Xbox360Report From(PadState s)
    {
        var buttons = Xbox360Buttons.None;
        foreach (var (from, to) in Map)
            if (s.Buttons.HasFlag(from))
                buttons |= to;
        buttons |= DpadMath.FromButtons(s.Buttons) switch
        {
            Dpad8.North => Xbox360Buttons.Up,
            Dpad8.NorthEast => Xbox360Buttons.Up | Xbox360Buttons.Right,
            Dpad8.East => Xbox360Buttons.Right,
            Dpad8.SouthEast => Xbox360Buttons.Down | Xbox360Buttons.Right,
            Dpad8.South => Xbox360Buttons.Down,
            Dpad8.SouthWest => Xbox360Buttons.Down | Xbox360Buttons.Left,
            Dpad8.West => Xbox360Buttons.Left,
            Dpad8.NorthWest => Xbox360Buttons.Up | Xbox360Buttons.Left,
            _ => Xbox360Buttons.None,
        };
        return new Xbox360Report(buttons, s.L2, s.R2, AxisX(s.LX), AxisY(s.LY), AxisX(s.RX), AxisY(s.RY));
    }

    private static short AxisX(byte v) => v < PadState.Center
        ? (short)((v - PadState.Center) * 256)
        : (short)((v - PadState.Center) * 32767 / 127);

    private static short AxisY(byte v) => v <= PadState.Center
        ? (short)((PadState.Center - v) * 32767 / PadState.Center)
        : (short)(-(v - PadState.Center) * 32768 / 127);
}
