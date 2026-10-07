using System.Globalization;

namespace CouchLink.Core;

/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen (clients can't display it: it isn't H.264); <c>--save-video=&lt;file&gt;</c>
/// makes a client save the H.264 it receives; <c>--windowed-player</c> opens the client's video in a
/// 1280x720 window instead of fullscreen, for testing host and client on one PC;
/// <c>--test-tone</c> makes the host send a beep instead of its sound; <c>--audio-loss=&lt;percent&gt;</c>
/// makes a client drop that share of audio packets, to hear the loss recovery.
/// </summary>
public sealed record DevOptions(
    bool TestPattern,
    string? SaveVideoPath,
    bool WindowedPlayer = false,
    bool TestTone = false,
    double AudioLossPercent = 0)
{
    private const string SaveVideo = "--save-video=";
    private const string AudioLoss = "--audio-loss=";

    public static DevOptions Parse(IEnumerable<string> args)
    {
        bool testPattern = false;
        string? savePath = null;
        bool windowed = false;
        bool testTone = false;
        double audioLoss = 0;
        foreach (var arg in args)
        {
            if (arg == "--test-pattern")
                testPattern = true;
            else if (arg.StartsWith(SaveVideo, StringComparison.Ordinal) && arg.Length > SaveVideo.Length)
                savePath = arg[SaveVideo.Length..];
            else if (arg == "--windowed-player")
                windowed = true;
            else if (arg == "--test-tone")
                testTone = true;
            else if (arg.StartsWith(AudioLoss, StringComparison.Ordinal)
                     && double.TryParse(arg[AudioLoss.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var loss))
                audioLoss = Math.Clamp(loss, 0, 100);
        }
        return new DevOptions(testPattern, savePath, windowed, testTone, audioLoss);
    }
}
