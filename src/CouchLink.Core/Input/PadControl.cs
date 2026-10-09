namespace CouchLink.Core.Input;

/// <summary>
/// Every DS4 control a key can be bound to. The right stick also follows the mouse while none of its keys is held.
/// Profile files use these names: renaming one breaks every profile that uses it.
/// </summary>
public enum PadControl
{
    LeftUp, LeftDown, LeftLeft, LeftRight,
    RightUp, RightDown, RightLeft, RightRight,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    Cross, Circle, Square, Triangle,
    L1, R1, L2, R2, L3, R3,
    Options, Share, Touchpad,
}
