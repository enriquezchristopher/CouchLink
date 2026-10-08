namespace CouchLink.Core.Input;

/// <summary>What <see cref="KeyLayout.Bind"/> did: refused a reserved key, or bound it, maybe taking it from another control.</summary>
public readonly record struct BindResult(bool Bound, PadControl? MovedFrom)
{
    public static readonly BindResult Reserved = new(false, null);
}

/// <summary>
/// Which keys drive which DS4 control. The defaults give a few controls two keys; a rebind gives the
/// control exactly one key and takes that key off any other control. Thread-safe: the editor binds
/// on the UI thread while the input loop reads every tick.
/// </summary>
public sealed class KeyLayout
{
    private static readonly ushort[] ReservedKeys =
        [VirtualKeys.Escape, VirtualKeys.F1, VirtualKeys.F2, VirtualKeys.LWin, VirtualKeys.RWin];

    private readonly Dictionary<PadControl, ushort[]> _bindings;
    private readonly Lock _gate = new();

    private KeyLayout(Dictionary<PadControl, ushort[]> bindings) => _bindings = bindings;

    public IReadOnlyList<ushort> KeysFor(PadControl control)
    {
        lock (_gate)
            return _bindings.TryGetValue(control, out var keys) ? keys : [];
    }

    /// <summary>Esc cancels a rebind, F1 and F2 toggle the player's panels, the Windows keys are blocked while playing.</summary>
    public static bool IsReserved(ushort key) => ReservedKeys.Contains(key);

    public BindResult Bind(PadControl control, ushort key)
    {
        if (IsReserved(key))
            return BindResult.Reserved;
        lock (_gate)
        {
            PadControl? movedFrom = null;
            foreach (var (other, keys) in _bindings.ToList())
            {
                if (other == control || !keys.Contains(key))
                    continue;
                _bindings[other] = keys.Where(k => k != key).ToArray();
                movedFrom = other;
            }
            _bindings[control] = [key];
            return new BindResult(true, movedFrom);
        }
    }

    public void ResetToDefault()
    {
        lock (_gate)
        {
            _bindings.Clear();
            foreach (var (control, keys) in Defaults())
                _bindings[control] = keys;
        }
    }

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(Defaults());

    private static Dictionary<PadControl, ushort[]> Defaults() => new()
    {
        [PadControl.LeftUp] = [VirtualKeys.Letter('W')],
        [PadControl.LeftLeft] = [VirtualKeys.Letter('A')],
        [PadControl.LeftDown] = [VirtualKeys.Letter('S')],
        [PadControl.LeftRight] = [VirtualKeys.Letter('D')],
        [PadControl.DpadUp] = [VirtualKeys.Up],
        [PadControl.DpadDown] = [VirtualKeys.Down],
        [PadControl.DpadLeft] = [VirtualKeys.Left],
        [PadControl.DpadRight] = [VirtualKeys.Right],
        [PadControl.Cross] = [VirtualKeys.Letter('K')],
        [PadControl.Square] = [VirtualKeys.Letter('J'), VirtualKeys.LButton],
        [PadControl.Circle] = [VirtualKeys.Letter('L')],
        [PadControl.Triangle] = [VirtualKeys.Letter('I')],
        [PadControl.L1] = [VirtualKeys.Letter('Q')],
        [PadControl.R1] = [VirtualKeys.Letter('E')],
        [PadControl.L2] = [VirtualKeys.LControl, VirtualKeys.RControl],
        [PadControl.R2] = [VirtualKeys.LShift, VirtualKeys.RShift],
        [PadControl.L3] = [VirtualKeys.Letter('F')],
        [PadControl.R3] = [VirtualKeys.MButton],
        [PadControl.Options] = [VirtualKeys.Return],
        [PadControl.Share] = [VirtualKeys.Back],
        [PadControl.Touchpad] = [VirtualKeys.Tab],
    };
}
