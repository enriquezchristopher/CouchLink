namespace CouchLink.App.Presentation;

/// <summary>Who Stop hosting would disconnect, for the confirm dialog. Null: nobody, stop at once.</summary>
internal static class StopHostingPrompt
{
    public static string? For(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => $"{names[0]} will be disconnected.",
        2 => $"{names[0]} and {names[1]} will be disconnected.",
        3 => $"{names[0]}, {names[1]} and {names[2]} will be disconnected.",
        _ => $"{names.Count} players will be disconnected.",
    };
}
