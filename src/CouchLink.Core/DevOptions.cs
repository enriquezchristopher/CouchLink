namespace CouchLink.Core;

/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen; <c>--save-video=&lt;file&gt;</c> makes a client save the H.264 it receives.
/// </summary>
public sealed record DevOptions(bool TestPattern, string? SaveVideoPath)
{
    private const string SaveVideo = "--save-video=";

    public static DevOptions Parse(IEnumerable<string> args)
    {
        bool testPattern = false;
        string? savePath = null;
        foreach (var arg in args)
        {
            if (arg == "--test-pattern")
                testPattern = true;
            else if (arg.StartsWith(SaveVideo, StringComparison.Ordinal) && arg.Length > SaveVideo.Length)
                savePath = arg[SaveVideo.Length..];
        }
        return new DevOptions(testPattern, savePath);
    }
}
