namespace CouchLink.Core;

/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen (clients can't display it: it isn't H.264); <c>--save-video=&lt;file&gt;</c>
/// makes a client save the H.264 it receives; <c>--windowed-player</c> opens the client's video in a
/// 1280x720 window instead of fullscreen, for testing host and client on one PC.
/// </summary>
public sealed record DevOptions(bool TestPattern, string? SaveVideoPath, bool WindowedPlayer = false)
{
    private const string SaveVideo = "--save-video=";

    public static DevOptions Parse(IEnumerable<string> args)
    {
        bool testPattern = false;
        string? savePath = null;
        bool windowed = false;
        foreach (var arg in args)
        {
            if (arg == "--test-pattern")
                testPattern = true;
            else if (arg.StartsWith(SaveVideo, StringComparison.Ordinal) && arg.Length > SaveVideo.Length)
                savePath = arg[SaveVideo.Length..];
            else if (arg == "--windowed-player")
                windowed = true;
        }
        return new DevOptions(testPattern, savePath, windowed);
    }
}
