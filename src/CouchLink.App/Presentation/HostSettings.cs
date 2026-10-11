using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CouchLink.Core.Pads;

namespace CouchLink.App.Presentation;

/// <summary>
/// The controller the host picked last, saved per PC in %LOCALAPPDATA%\CouchLink\settings.json as
/// <c>{ "hostController": "xbox360" }</c>. DualShock 4 by default. A missing file reads as DualShock 4; an unreadable
/// one or an unknown value reads as DualShock 4 and is logged, never thrown; a file that can't be written keeps the
/// choice for this run. Other keys in the file are kept when it is saved.
/// </summary>
internal sealed class HostSettings
{
    private const string Key = "hostController";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string> _log;
    private PadKind _controller;

    public HostSettings(string path, Action<string> log)
    {
        _path = path;
        _log = log;
        _controller = Read();
    }

    public static string DefaultPath => MotionSettings.DefaultPath;

    public PadKind Controller
    {
        get => _controller;
        set
        {
            if (value == _controller)
                return;
            _controller = value;
            Save();
        }
    }

    private static string Name(PadKind kind) => kind == PadKind.Xbox360 ? "xbox360" : "dualshock4";

    private PadKind Read()
    {
        if (!File.Exists(_path))
            return PadKind.DualShock4;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_path))?.AsObject()[Key];
            if (node is null)
                return PadKind.DualShock4;
            return node.GetValue<string>() switch
            {
                "xbox360" => PadKind.Xbox360,
                "dualshock4" => PadKind.DualShock4,
                var other => throw new FormatException($"unknown controller \"{other}\""),
            };
        }
        catch (Exception ex)
        {
            _log($"Settings: could not read the host controller from {_path}, so it is DualShock 4: {ex.Message}");
            return PadKind.DualShock4;
        }
    }

    private void Save()
    {
        try
        {
            var root = ReadObject() ?? [];
            root[Key] = Name(_controller);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(Indented));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log($"Settings: could not save {_path}; the host controller is {PadKinds.Label(_controller)} until CouchLink closes: {ex.Message}");
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
