using CouchLink.Core.Session;

namespace CouchLink.App.Presentation;

/// <summary>
/// What the Session screen says in each client state. <see cref="Step"/> is the StepTracker's current
/// step: 0 Connect, 1 Host lets you in, 2 Play.
/// </summary>
internal readonly record struct SessionText(string Heading, string Hint, int Step, string LeaveText, bool Playing, bool Reconnecting)
{
    /// <summary>Null for <see cref="ClientState.Ended"/>: the screen is about to go, so it keeps what it shows.</summary>
    public static SessionText? For(ClientState state, string host, byte slot) => state switch
    {
        ClientState.Connecting => new($"Connecting to {host}…", "", 0, "Cancel", false, false),
        ClientState.Waiting => new($"Waiting for {host} to let you in", "The host sees a popup and can allow or deny.", 1, "Cancel", false, false),
        ClientState.Playing => new($"You're P{slot}", $"Playing on {host}", 2, "Leave", true, false),
        ClientState.Reconnecting => new($"Reconnecting to {host}…", "Your slot is kept for a minute.", 2, "Leave", false, true),
        _ => null,
    };
}
