namespace CouchLink.Core.Input;

/// <summary>Which keys drive which DS4 control. Several keys may drive one control.</summary>
public sealed class KeyLayout
{
    private readonly Dictionary<PadControl, ushort[]> _bindings;

    private KeyLayout(Dictionary<PadControl, ushort[]> bindings) => _bindings = bindings;

    public IReadOnlyList<ushort> KeysFor(PadControl control) =>
        _bindings.TryGetValue(control, out var keys) ? keys : [];

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(new Dictionary<PadControl, ushort[]>
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
        [PadControl.L2] = [VirtualKeys.Control],
        [PadControl.R2] = [VirtualKeys.Shift],
        [PadControl.L3] = [VirtualKeys.Letter('F')],
        [PadControl.R3] = [VirtualKeys.MButton],
        [PadControl.Options] = [VirtualKeys.Return],
        [PadControl.Share] = [VirtualKeys.Back],
        [PadControl.Touchpad] = [VirtualKeys.Tab],
    });
}
