namespace CouchLink.Core.Input;

[Flags]
public enum PadButtons : uint
{
    None = 0,
    Cross = 1u << 0,
    Circle = 1u << 1,
    Square = 1u << 2,
    Triangle = 1u << 3,
    L1 = 1u << 4,
    R1 = 1u << 5,
    L2 = 1u << 6,
    R2 = 1u << 7,
    L3 = 1u << 8,
    R3 = 1u << 9,
    Options = 1u << 10,
    Share = 1u << 11,
    Touchpad = 1u << 12,
    PS = 1u << 13,
    DpadUp = 1u << 14,
    DpadDown = 1u << 15,
    DpadLeft = 1u << 16,
    DpadRight = 1u << 17,

    /// <summary>Every defined button bit; anything else is unknown.</summary>
    Known = (1u << 18) - 1,
}
