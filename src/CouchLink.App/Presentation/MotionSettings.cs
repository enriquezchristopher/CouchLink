using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CouchLink.App.Presentation;

/// <summary>
/// Help → Reduce motion, saved per PC in %LOCALAPPDATA%\CouchLink\settings.json as <c>{ "reduceMotion": true }</c>.
/// Off by default. A missing, unreadable or corrupt file reads as off and is logged, never thrown; a file that
/// can't be written keeps the choice for this run. Other keys in the file are kept when it is saved.
/// </summary>
internal sealed class MotionSettings : INotifyPropertyChanged
{
    private const string Key = "reduceMotion";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string> _log;
    private bool _reduceMotion;

    public MotionSettings(string path, Action<string> log)
    {
        _path = path;
        _log = log;
        _reduceMotion = Read();
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchLink", "settings.json");

    /// <summary>The setting changed (after it was saved).</summary>
    public event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True: transitions are instant or a short plain fade. The countdown bar and spinners still move.</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (value == _reduceMotion)
                return;
            _reduceMotion = value;
            Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReduceMotion)));
            Changed?.Invoke();
        }
    }

    private bool Read()
    {
        if (!File.Exists(_path))
            return false;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_path))?.AsObject()[Key];
            return node is not null && node.GetValue<bool>();
        }
        catch (Exception ex)
        {
            _log($"Settings: could not read {_path}, so motion stays on: {ex.Message}");
            return false;
        }
    }

    private void Save()
    {
        try
        {
            var root = ReadObject() ?? [];
            root[Key] = _reduceMotion;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(Indented));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log($"Settings: could not save {_path}; Reduce motion is {(_reduceMotion ? "on" : "off")} until CouchLink closes: {ex.Message}");
        }
    }

    /// <summary>The file's object, to keep its other keys; null when there is none or it is not one.</summary>
    private JsonObject? ReadObject()
    {
        try
        {
            return File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
