namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Saves crash reports where the user can find them, falls back to %TEMP% when
/// that fails, keeps the newest <see cref="Keep"/>, and remembers which ones the
/// user has already been shown.
/// </summary>
public sealed class CrashReportStore
{
    public const int Keep = 20;
    private const string Pattern = "couchlink-crash-*.txt";
    private const string ShownIndex = "shown.txt";

    private readonly string _fallback;

    public CrashReportStore(string primaryDirectory, string fallbackDirectory)
    {
        PrimaryDirectory = primaryDirectory;
        _fallback = fallbackDirectory;
    }

    public string PrimaryDirectory { get; }

    public static CrashReportStore Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchLink", "CrashReports"),
        Path.Combine(Path.GetTempPath(), "CouchLink", "CrashReports"));

    /// <summary>Writes the report and returns its full path. Throws IOException only if both folders fail.</summary>
    public string Save(string content, DateTime localNow)
    {
        var name = $"couchlink-crash-{localNow:yyyyMMdd-HHmmss}";
        Exception? firstError = null;
        foreach (var directory in new[] { PrimaryDirectory, _fallback })
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = UniquePath(directory, name);
                File.WriteAllText(path, content);
                Prune(directory);
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                firstError ??= e;
            }
        }
        throw new IOException($"Could not save the crash report: {firstError?.Message}", firstError);
    }

    /// <summary>Reports the user has not been shown yet, oldest first.</summary>
    public IReadOnlyList<string> Unshown()
    {
        var result = new List<string>();
        foreach (var directory in new[] { PrimaryDirectory, _fallback })
        {
            if (!Directory.Exists(directory))
                continue;
            var shown = ReadShown(directory);
            result.AddRange(Directory.GetFiles(directory, Pattern)
                .Where(f => !shown.Contains(Path.GetFileName(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal));
        }
        return result;
    }

    public void MarkShown(string path)
    {
        try
        {
            var index = Path.Combine(Path.GetDirectoryName(path)!, ShownIndex);
            File.AppendAllText(index, Path.GetFileName(path) + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worst case the dialog shows again next start; never crash over it.
        }
    }

    private static string UniquePath(string directory, string name)
    {
        var path = Path.Combine(directory, name + ".txt");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(directory, $"{name}-{n}.txt");
        return path;
    }

    private static void Prune(string directory)
    {
        var files = Directory.GetFiles(directory, Pattern)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
        foreach (var old in files.Take(Math.Max(0, files.Length - Keep)))
            File.Delete(old);
    }

    private static HashSet<string> ReadShown(string directory)
    {
        var index = Path.Combine(directory, ShownIndex);
        return File.Exists(index) ? File.ReadAllLines(index).ToHashSet() : [];
    }
}
