using CouchLink.Core.Pads;

namespace CouchLink.Core.Input;

/// <summary>
/// What the editor's controller picture shows for one pad state: the lit parts and how far each stick leans
/// (-1..1, Y down as on screen). The D-pad is lit as the game sees it (opposite directions cancel); a trigger
/// lights when pressed past <see cref="TriggerThreshold"/>. Xbox 360 has no touchpad.
/// </summary>
public readonly record struct PadPicture(IReadOnlySet<PadControl> Lit, double LeftX, double LeftY, double RightX, double RightY)
{
    public const byte TriggerThreshold = 32;

    private static readonly (PadButtons Button, PadControl Part)[] Buttons =
    [
        (PadButtons.Cross, PadControl.Cross), (PadButtons.Circle, PadControl.Circle),
        (PadButtons.Square, PadControl.Square), (PadButtons.Triangle, PadControl.Triangle),
        (PadButtons.L1, PadControl.L1), (PadButtons.R1, PadControl.R1),
        (PadButtons.L3, PadControl.L3), (PadButtons.R3, PadControl.R3),
        (PadButtons.Options, PadControl.Options), (PadButtons.Share, PadControl.Share),
        (PadButtons.Touchpad, PadControl.Touchpad),
    ];

    public static PadPicture Neutral { get; } = new(new HashSet<PadControl>(), 0, 0, 0, 0);

    public static PadPicture From(PadState s, PadKind kind)
    {
        var lit = new HashSet<PadControl>();
        foreach (var (button, part) in Buttons)
            if (s.Buttons.HasFlag(button))
                lit.Add(part);
        if (kind == PadKind.Xbox360)
            lit.Remove(PadControl.Touchpad);
        if (s.L2 > TriggerThreshold || s.Buttons.HasFlag(PadButtons.L2))
            lit.Add(PadControl.L2);
        if (s.R2 > TriggerThreshold || s.Buttons.HasFlag(PadButtons.R2))
            lit.Add(PadControl.R2);
        var d = DpadMath.FromButtons(s.Buttons);
        if (d is Dpad8.North or Dpad8.NorthEast or Dpad8.NorthWest) lit.Add(PadControl.DpadUp);
        if (d is Dpad8.South or Dpad8.SouthEast or Dpad8.SouthWest) lit.Add(PadControl.DpadDown);
        if (d is Dpad8.West or Dpad8.NorthWest or Dpad8.SouthWest) lit.Add(PadControl.DpadLeft);
        if (d is Dpad8.East or Dpad8.NorthEast or Dpad8.SouthEast) lit.Add(PadControl.DpadRight);
        return new PadPicture(lit, Lean(s.LX), Lean(s.LY), Lean(s.RX), Lean(s.RY));
    }

    private static double Lean(byte v) => Math.Clamp((v - PadState.Center) / 127.0, -1, 1);
}
