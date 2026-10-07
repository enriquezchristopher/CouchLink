using CouchLink.Core.Audio;

namespace CouchLink.Core.Video;

/// <summary>The player's on-screen text: the F2 stats, and why there is no live picture.</summary>
public static class OverlayText
{
    public const string Waiting = "Waiting for the host's picture...";
    public const string Paused = "Host screen paused";
    public const string NoPicture = "No picture from the host";
    public const string LeaveHint = "Ctrl+Alt+Q to leave";

    public static string Stats(StatsSample? sample, string decoder, string? audio = null)
    {
        string video;
        if (sample is not { } s)
            video = "Collecting stats...";
        else
        {
            string latency = s.Latency is { } total && s.Network is { } network
                ? $"Latency ~{Ms(total)} ms (host {Ms(s.HostDelay)} + network {Ms(network)} + client {Ms(s.ClientDelay)})"
                : "Latency measuring...";
            video = $"{s.Fps:0} fps  {s.Mbps:0.0} Mbps  ({decoder})\n" +
                    $"Packet loss {s.LossPercent:0.0}%  FEC repairs {s.FecRepairs}/s\n" +
                    latency;
        }
        return audio is null ? video : $"{video}\n{audio}";
    }

    /// <summary>The client's audio, for the F2 overlay and the dev window; <paramref name="output"/> is the device or why it is silent.</summary>
    public static string Audio(AudioClientStats s, string output) =>
        s.Packets == 0
            ? $"Audio: nothing from the host yet ({output})"
            : $"Audio buffer {s.BufferMs:0} ms  repaired {s.Repaired}  concealed {s.Concealed}  late {s.Late}\n" +
              $"  {s.Packets} packets  drift {s.DriftCorrections}  ({output})";

    /// <summary>
    /// A centred message when there is no live picture, or null. When nothing is coming (yet), it
    /// also says how to leave: the fullscreen player has no visible controls.
    /// </summary>
    public static string? Status(bool anyFrameShown, bool hostPaused, TimeSpan sinceLastFrame, TimeSpan quietAfter) =>
        !anyFrameShown ? $"{Waiting}\n{LeaveHint}"
        : sinceLastFrame >= quietAfter ? $"{NoPicture}\n{LeaveHint}"
        : hostPaused ? Paused
        : null;

    private static string Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("0");
}
