using CouchLink.Core.Input;

namespace CouchLink.Core.Pads;

/// <summary>
/// Logic for the game-free pad isolation check: every pad gets a unique state,
/// and each state must show up on exactly one device, a different one per pad.
/// </summary>
public static class IsolationCheck
{
    private static readonly PadButtons[] SignatureButtons =
    [
        PadButtons.Cross, PadButtons.Circle, PadButtons.Square,
        PadButtons.Triangle, PadButtons.L1, PadButtons.R1,
        PadButtons.L3, PadButtons.R3, PadButtons.Share,
    ];

    /// <summary>A state unique to pad <paramref name="index"/> (0..8).</summary>
    public static PadState SignatureFor(int index) =>
        PadState.Neutral with { Buttons = SignatureButtons[index], LX = (byte)(30 + 20 * index) };

    /// <summary>Index of the only device showing <paramref name="target"/>, or null if none or several do.</summary>
    public static int? FindExactlyOne(PadState target, IReadOnlyList<PadState?> read)
    {
        int? found = null;
        for (int i = 0; i < read.Count; i++)
        {
            if (read[i] != target)
                continue;
            if (found is not null)
                return null;
            found = i;
        }
        return found;
    }

    /// <summary>True when every pad matched a device and no two pads matched the same one.</summary>
    public static bool AllDistinct(IReadOnlyList<int?> matches) =>
        matches.Count > 0
        && matches.All(m => m is not null)
        && matches.Distinct().Count() == matches.Count;
}
