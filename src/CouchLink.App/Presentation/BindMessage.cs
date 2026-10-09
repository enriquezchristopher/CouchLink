using CouchLink.Core.Input;

namespace CouchLink.App.Presentation;

/// <summary>What the controls editor's banner says after a key is pressed while a row listens.</summary>
internal static class BindMessage
{
    public static string Reserved(ushort key) => $"{KeyNames.Of(key)} is reserved. Press another key.";

    /// <summary>Null when the key wasn't on another control. "Has no key now" only when that control has none left.</summary>
    public static string? Moved(ushort key, PadControl? movedFrom, int keysLeftOnMovedFrom)
    {
        if (movedFrom is not { } from)
            return null;
        string moved = $"{KeyNames.Of(key)} moved here from {KeyNames.Of(from)}.";
        return keysLeftOnMovedFrom == 0 ? $"{moved} {KeyNames.Of(from)} has no key now." : moved;
    }
}
