using CouchLink.Core.Audio;
using CouchLink.Core.Input;

namespace CouchLink.Core.Video;

/// <summary>The player's on-screen text: the F2 stats, the F1 controls, the start hint, and why there is no live picture.</summary>
public static class OverlayText
{
    public const string Waiting = "Waiting for the host's picture...";
    public const string Paused = "Host screen paused";
    public const string NoPicture = "No picture from the host";
    public const string LeaveHint = "Ctrl+Alt+Q to leave";
    public const string Reconnecting = "Reconnecting...";
    public const string StartHint = "F1: controls · Ctrl+Alt+Q: leave";

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
    /// A centred message when there is no live picture, or null. A session line (e.g.
    /// "Reconnecting...") wins over everything. When nothing is coming (yet), it also says how to
    /// leave: the fullscreen player has no visible controls.
    /// </summary>
    public static string? Status(bool anyFrameShown, bool hostPaused, TimeSpan sinceLastFrame, TimeSpan quietAfter,
        string? session = null) =>
        session is not null ? $"{session}\n{LeaveHint}"
        : !anyFrameShown ? $"{Waiting}\n{LeaveHint}"
        : sinceLastFrame >= quietAfter ? $"{NoPicture}\n{LeaveHint}"
        : hostPaused ? Paused
        : null;

    /// <summary>The F1 panel: every control and its keys, grouped as in the editor, read when drawn.</summary>
    public static string Controls(ControlSettings settings)
    {
        var lines = new List<string> { "Controls (F1 hides, Ctrl+Alt+C changes keys)" };
        foreach (var (_, controls) in KeyNames.Groups)
        {
            lines.Add("");
            foreach (var control in controls)
                lines.Add($"{KeyNames.Of(control),-18}{KeyNames.Describe(settings.Layout, control)}");
        }
        lines.Add("");
        lines.Add($"{"Right stick",-18}Mouse (sensitivity {settings.SensitivityStep}{(settings.InvertY ? ", inverted" : "")})");
        return string.Join('\n', lines);
    }

    private static string Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("0");
}
