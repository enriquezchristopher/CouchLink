using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Presentation;

/// <summary>The controls editor's row names and its Find box.</summary>
internal static class ControlFilter
{
    /// <summary>Under a "Left stick" or "D-pad" heading a row only needs the direction.</summary>
    public static string RowName(PadControl control, PadKind kind = PadKind.DualShock4) => control switch
    {
        PadControl.LeftUp or PadControl.RightUp or PadControl.DpadUp => "Up",
        PadControl.LeftDown or PadControl.RightDown or PadControl.DpadDown => "Down",
        PadControl.LeftLeft or PadControl.RightLeft or PadControl.DpadLeft => "Left",
        PadControl.LeftRight or PadControl.RightRight or PadControl.DpadRight => "Right",
        _ => KeyNames.Of(control, kind),
    };

    /// <summary>True when the query is empty or found in the control's full name, its group or its label.</summary>
    public static bool Matches(PadControl control, string group, string? label, string? query, PadKind kind = PadKind.DualShock4)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0)
            return true;
        return Has(KeyNames.Of(control, kind)) || Has(group) || (label is not null && Has(label));

        bool Has(string text) => text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
