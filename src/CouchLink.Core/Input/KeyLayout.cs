namespace CouchLink.Core.Input;

/// <summary>What <see cref="KeyLayout.Bind"/> did: refused a reserved key, or bound it, maybe taking it from another control.</summary>
public readonly record struct BindResult(bool Bound, PadControl? MovedFrom)
{
    public static readonly BindResult Reserved = new(false, null);
}

/// <summary>
/// Which keys drive which DS4 control. The defaults give a few controls two keys; a rebind gives the
/// control exactly one key and takes that key off any other control. The bindings are one immutable
/// snapshot (<see cref="Current"/>) replaced whole on every edit: the input loop reads it once per tick,
/// so an edit or a profile load can never land halfway through a tick.
/// </summary>
public sealed class KeyLayout
{
    private static readonly ushort[] ReservedKeys =
        [VirtualKeys.Escape, VirtualKeys.F1, VirtualKeys.F2, VirtualKeys.LWin, VirtualKeys.RWin];

    private readonly Lock _gate = new(); // one writer at a time; readers never wait
    private IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> _current;

    private KeyLayout(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> bindings) => _current = bindings;

    /// <summary>Every binding, as a snapshot that never changes once returned.</summary>
    public IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Current => Volatile.Read(ref _current);

    public IReadOnlyList<ushort> KeysFor(PadControl control) =>
        Current.TryGetValue(control, out var keys) ? keys : [];

    /// <summary>Esc cancels a rebind, F1 and F2 toggle the player's panels, the Windows keys are blocked while playing.</summary>
    public static bool IsReserved(ushort key) => ReservedKeys.Contains(key);

    public BindResult Bind(PadControl control, ushort key)
    {
        if (IsReserved(key))
            return BindResult.Reserved;
        lock (_gate)
        {
            var current = _current;
            var next = new Dictionary<PadControl, IReadOnlyList<ushort>>(current);
            PadControl? movedFrom = null;
            foreach (var (other, keys) in current)
            {
                if (other == control || !keys.Contains(key))
                    continue;
                next[other] = keys.Where(k => k != key).ToArray();
                movedFrom = other;
            }
            next[control] = [key];
            Volatile.Write(ref _current, next);
            return new BindResult(true, movedFrom);
        }
    }

    /// <summary>Swaps in a whole layout (a loaded profile). The caller has checked reserved keys and duplicates.</summary>
    public void Replace(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> keys)
    {
        IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> next =
            keys.ToDictionary(p => p.Key, p => (IReadOnlyList<ushort>)p.Value.ToArray());
        lock (_gate)
            Volatile.Write(ref _current, next);
    }

    public void ResetToDefault()
    {
        lock (_gate)
            Volatile.Write(ref _current, Defaults());
    }

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(Defaults());

    private static IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Defaults() =>
        new Dictionary<PadControl, IReadOnlyList<ushort>>
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
