namespace CouchLink.Core.Input;

/// <summary>
/// Full DualShock 4 state. Axes: 128 = center; X 0 = left, 255 = right;
/// Y 0 = up, 255 = down. Triggers: 0 = released, 255 = fully pressed.
/// Never use default(PadState): its axes are 0 (stick pushed up-left).
/// </summary>
public readonly record struct PadState(
    PadButtons Buttons, byte LX, byte LY, byte RX, byte RY, byte L2, byte R2)
{
    public const byte Center = 128;

    public static PadState Neutral { get; } =
        new(PadButtons.None, Center, Center, Center, Center, 0, 0);
}
