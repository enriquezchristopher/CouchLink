namespace CouchLink.Core.Input;

/// <summary>
/// Tracks held keys and mouse movement and turns them into a DS4 state. Right-stick keys, while held, override the mouse.
/// Thread-safe: input events arrive on the UI thread, Tick runs on the send loop, edits come from the editor.
/// </summary>
public sealed class InputMapper : IDisposable
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
    private readonly ControlSettings? _settings;

    public InputMapper(KeyLayout layout, MouseStick mouse)
    {
        _layout = layout;
        _mouse = mouse;
    }

    /// <summary>
    /// Follows the shared controls: an edit counts from the next tick, releases held keys (nothing
    /// stays pressed across a rebind) and applies the mouse settings. Dispose to stop following.
    /// </summary>
    public InputMapper(ControlSettings settings) : this(settings.Layout, new MouseStick())
    {
        _settings = settings;
        ApplyMouseSettings();
        settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        lock (_gate)
        {
            _held.Clear();
            _mouse.Reset();
            ApplyMouseSettings();
        }
    }

    private void ApplyMouseSettings()
    {
        _mouse.Sensitivity = _settings!.Sensitivity;
        _mouse.InvertY = _settings.InvertY;
    }

    public void Dispose()
    {
        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;
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
            var layout = _layout.Current; // one snapshot for the whole tick
            var buttons = PadButtons.None;
            foreach (var (control, button) in ButtonMap)
                if (IsDown(layout, control))
                    buttons |= button;

            var (lx, ly) = StickMath.FromDirections(
                IsDown(layout, PadControl.LeftUp), IsDown(layout, PadControl.LeftDown),
                IsDown(layout, PadControl.LeftLeft), IsDown(layout, PadControl.LeftRight));
            bool rightUp = IsDown(layout, PadControl.RightUp), rightDown = IsDown(layout, PadControl.RightDown);
            bool rightLeft = IsDown(layout, PadControl.RightLeft), rightRight = IsDown(layout, PadControl.RightRight);
            byte rx, ry;
            if (rightUp || rightDown || rightLeft || rightRight)
            {
                // Keys win: a key move always does the same thing, even if the mouse gets bumped. Dropping the
                // mouse's movement means releasing the keys centres the stick instead of replaying a flick.
                (rx, ry) = StickMath.FromDirections(rightUp, rightDown, rightLeft, rightRight);
                _mouse.Reset();
            }
            else
            {
                (rx, ry) = _mouse.Update(dtSeconds);
            }
            byte l2 = buttons.HasFlag(PadButtons.L2) ? (byte)255 : (byte)0;
            byte r2 = buttons.HasFlag(PadButtons.R2) ? (byte)255 : (byte)0;
            return new PadState(buttons, lx, ly, rx, ry, l2, r2);
        }
    }

    private bool IsDown(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> layout, PadControl control)
    {
        if (!layout.TryGetValue(control, out var keys))
            return false;
        foreach (var key in keys)
            if (_held.Contains(key))
                return true;
        return false;
    }
}
