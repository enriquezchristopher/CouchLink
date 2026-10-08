using System.Text;

namespace CouchLink.Core.Input;

/// <summary>A profile file found in the profiles folder. <see cref="DisplayName"/> adds the file name when two profiles share a name.</summary>
public sealed record ProfileEntry(string Path, ControlProfile Profile, string DisplayName);

/// <summary>
/// Profile files on disk: the <c>profiles</c> folder next to the exe, and any file the player browses to.
/// Never throws for I/O: problems come back as messages, and skipped or failed files go to the log.
/// </summary>
public sealed class ProfileStore(string folder, Action<string>? log = null)
{
    private const string UnsafeFileNameChars = "\\/:*?\"<>|";

    /// <summary>The <c>profiles</c> folder next to CouchLink.App.exe.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "profiles");

    public string Folder { get; } = folder;

    /// <summary>Every profile that loads from the folder's top-level *.json files, sorted by name. Read fresh each call.</summary>
    public IReadOnlyList<ProfileEntry> List()
    {
        string[] files;
        try
        {
            if (!Directory.Exists(Folder))
                return [];
            files = Directory.GetFiles(Folder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"profiles folder can't be read: {e.Message}");
            return [];
        }

        var found = new List<(string Path, ControlProfile Profile)>();
        foreach (var file in files)
        {
            var result = Read(file);
            if (result.Profile is { } profile)
                found.Add((file, profile));
            else
                log?.Invoke($"profile skipped: {Path.GetFileName(file)}: {result.Error}");
        }

        var shared = found
            .GroupBy(f => f.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return found
            .OrderBy(f => f.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .Select(f => new ProfileEntry(f.Path, f.Profile,
                shared.Contains(f.Profile.Name) ? $"{f.Profile.Name} ({Path.GetFileName(f.Path)})" : f.Profile.Name))
            .ToList();
    }

    /// <summary>Reads one profile file: refused above 64 KB, UTF-8 with or without a byte-order mark.</summary>
    public static ProfileResult Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > ProfileFile.MaxBytes)
                return ProfileResult.Failure("File is too large for a profile");
            return ProfileFile.Load(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ProfileResult.Failure($"Couldn't read {Path.GetFileName(path)}: {e.Message}");
        }
    }

    /// <summary>Writes through a temp file in the same folder, so a failed save never leaves half a file. Null, or why it failed.</summary>
    public string? Write(string path, ControlProfile profile)
    {
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, ProfileFile.Save(profile), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            log?.Invoke($"profile not saved: {path}: {e.Message}");
            return $"Couldn't save {Path.GetFileName(path)}: {e.Message}";
        }
    }

    /// <summary>Creates the folder for the first Save as…; false (and logged) when it can't be created.</summary>
    public bool TryCreateFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"profiles folder can't be created: {e.Message}");
            return false;
        }
    }

    /// <summary>A file name for a profile name: characters Windows refuses become '-'.</summary>
    public static string SuggestedFileName(string name)
    {
        var chars = name.Trim().Select(c => c < ' ' || UnsafeFileNameChars.Contains(c) ? '-' : c).ToArray();
        string stem = new string(chars).Trim(' ', '.');
        return (stem.Length == 0 ? "profile" : stem) + ".json";
    }
}
