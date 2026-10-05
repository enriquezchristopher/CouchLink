namespace CouchLink.Core.Input;

/// <summary>Every DS4 control a key can be bound to. The right stick comes from the mouse.</summary>
public enum PadControl
{
    LeftUp, LeftDown, LeftLeft, LeftRight,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    Cross, Circle, Square, Triangle,
    L1, R1, L2, R2, L3, R3,
    Options, Share, Touchpad,
}
