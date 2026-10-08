namespace CouchLink.Core.Startup;

public enum InstanceRole
{
    /// <summary>The only copy: start normally and listen for later launches.</summary>
    Run,

    /// <summary>Another copy runs: tell it to come forward, then exit.</summary>
    HandOff,

    /// <summary>--windowed-player: one-PC testing runs two copies, so don't check.</summary>
    Bypass,
}

/// <summary>One CouchLink per Windows sign-in, except with --windowed-player.</summary>
public static class InstanceDecision
{
    public static bool ShouldCheck(DevOptions options) => !options.WindowedPlayer;

    public static InstanceRole Decide(DevOptions options, bool ownsMutex) =>
        !ShouldCheck(options) ? InstanceRole.Bypass
        : ownsMutex ? InstanceRole.Run
        : InstanceRole.HandOff;
}
