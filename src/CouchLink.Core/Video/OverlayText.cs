namespace CouchLink.Core.Video;

/// <summary>The player's on-screen text: the F2 stats, and why there is no live picture.</summary>
public static class OverlayText
{
    public const string Waiting = "Waiting for the host's picture...";
    public const string Paused = "Host screen paused";

    public static string Stats(StatsSample? sample, string decoder)
    {
        if (sample is not { } s)
            return "Collecting stats...";
        string latency = s.Latency is { } total && s.Network is { } network
            ? $"Latency ~{Ms(total)} ms (host {Ms(s.HostDelay)} + network {Ms(network)} + client {Ms(s.ClientDelay)})"
            : "Latency measuring...";
        return $"{s.Fps:0} fps  {s.Mbps:0.0} Mbps  ({decoder})\n" +
               $"Packet loss {s.LossPercent:0.0}%  FEC repairs {s.FecRepairs}/s\n" +
               latency;
    }

    /// <summary>A centred message when there is no live picture, or null.</summary>
    public static string? Status(bool anyFrameShown, bool hostPaused) =>
        !anyFrameShown ? Waiting : hostPaused ? Paused : null;

    private static string Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("0");
}
