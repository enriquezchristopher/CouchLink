namespace CouchLink.Core.Input;

/// <summary>
/// Tracks held keys and mouse movement and turns them into a DS4 state.
/// Thread-safe: input events arrive on the UI thread, Tick runs on the send loop.
/// </summary>
public sealed class InputMapper
{
    private static readonly (PadControl Control, PadButtons Button)[] ButtonMap =
    [
        (PadControl.Cross, PadButtons.Cross),
        (PadControl.Circle, PadButtons.Circle),
        (PadControl.Square, PadButtons.Square),
        (PadControl.Triangle, PadButtons.Triangle),
        (PadControl.L1, PadButtons.L1),
        (PadControl.R1, PadButtons.R1),
        (PadControl.L2, PadButtons.L2),
        (PadControl.R2, PadButtons.R2),
        (PadControl.L3, PadButtons.L3),
        (PadControl.R3, PadButtons.R3),
        (PadControl.Options, PadButtons.Options),
        (PadControl.Share, PadButtons.Share),
        (PadControl.Touchpad, PadButtons.Touchpad),
        (PadControl.DpadUp, PadButtons.DpadUp),
        (PadControl.DpadDown, PadButtons.DpadDown),
        (PadControl.DpadLeft, PadButtons.DpadLeft),
        (PadControl.DpadRight, PadButtons.DpadRight),
    ];

    private readonly KeyLayout _layout;
    private readonly MouseStick _mouse;
    private readonly HashSet<ushort> _held = [];
    private readonly Lock _gate = new();

    public InputMapper(KeyLayout layout, MouseStick mouse)
    {
        _layout = layout;
        _mouse = mouse;
    }

    public void KeyDown(ushort vk) { lock (_gate) _held.Add(vk); }

    public void KeyUp(ushort vk) { lock (_gate) _held.Remove(vk); }

    public void MouseMove(int dx, int dy) { lock (_gate) _mouse.AddDelta(dx, dy); }

    /// <summary>Releases everything, e.g. when the window loses focus.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _held.Clear();
            _mouse.Reset();
        }
    }

    /// <summary>
    /// Drops held keys that are no longer physically down. Catches key-ups lost to
    /// a secure-desktop switch (Ctrl+Alt+Del, Win+L, UAC) where no deactivate fires.
    /// </summary>
    public void SyncHeld(Func<ushort, bool> isPhysicallyDown)
    {
        lock (_gate)
            _held.RemoveWhere(vk => !isPhysicallyDown(vk));
    }

    public PadState Tick(double dtSeconds)
    {
        lock (_gate)
        {
            var buttons = PadButtons.None;
            foreach (var (control, button) in ButtonMap)
                if (IsDown(control))
                    buttons |= button;

            var (lx, ly) = StickMath.FromDirections(
                IsDown(PadControl.LeftUp), IsDown(PadControl.LeftDown),
                IsDown(PadControl.LeftLeft), IsDown(PadControl.LeftRight));
            var (rx, ry) = _mouse.Update(dtSeconds);
            byte l2 = buttons.HasFlag(PadButtons.L2) ? (byte)255 : (byte)0;
            byte r2 = buttons.HasFlag(PadButtons.R2) ? (byte)255 : (byte)0;
            return new PadState(buttons, lx, ly, rx, ry, l2, r2);
        }
    }

    private bool IsDown(PadControl control)
    {
        foreach (var key in _layout.KeysFor(control))
            if (_held.Contains(key))
                return true;
        return false;
    }
}
