using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CouchLink.Core.Input;

/// <summary>What <see cref="ProfileFile.Load"/> gave back: a whole profile, or why the file was refused.</summary>
public sealed record ProfileResult(ControlProfile? Profile, string? Error)
{
    public static ProfileResult Success(ControlProfile profile) => new(profile, null);
    public static ProfileResult Failure(string error) => new(null, error);
}

/// <summary>
/// Reads and writes controller profile files (controller profiles design, section 2). Works on strings;
/// <see cref="ProfileStore"/> does the disk. Load returns a whole profile or the first problem found,
/// never part of a profile.
/// </summary>
public static class ProfileFile
{
    public const int Format = 1;
    public const int MaxNameLength = 60, MaxGameLength = 60, MaxLabelLength = 24;
    public const int MaxBytes = 64 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "Café", not "Café"
    };

    private static readonly Dictionary<string, PadControl> ControlNames =
        Enum.GetValues<PadControl>().ToDictionary(c => c.ToString(), StringComparer.OrdinalIgnoreCase);

    public static ProfileResult Load(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, ReadOptions);
            return ProfileResult.Success(Read(document.RootElement));
        }
        catch (JsonException e)
        {
            return ProfileResult.Failure($"Not a valid profile file (line {(e.LineNumber ?? 0) + 1}): {ParserMessage(e)}");
        }
        catch (ProfileException e)
        {
            return ProfileResult.Failure(e.Message);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // Text the parser accepts but can't turn into a string, e.g. half an emoji escape ("\uD83D").
            return ProfileResult.Failure($"Not a valid profile file: {e.Message}");
        }
    }

    public static string Save(ControlProfile profile)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, WriteOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("name", profile.Name);
            if (profile.Game is { } game)
                w.WriteString("game", game);
            w.WriteNumber("sensitivity", profile.SensitivityStep);
            w.WriteBoolean("invertY", profile.InvertY);
            w.WriteStartObject("controls");
            foreach (var (_, controls) in KeyNames.Groups)
            {
                foreach (var control in controls)
                {
                    IReadOnlyList<ushort> keys = profile.Keys.TryGetValue(control, out var k) ? k : [];
                    var label = profile.Labels.GetValueOrDefault(control);
                    if (keys.Count == 0 && label is null)
                        continue;
                    w.WriteStartObject(control.ToString());
                    if (keys.Count > 0)
                    {
                        w.WriteStartArray("keys");
                        foreach (var key in keys)
                            w.WriteStringValue(ProfileKeys.IdOf(key));
                        w.WriteEndArray();
                    }
                    if (label is not null)
                        w.WriteString("label", label);
                    w.WriteEndObject();
                }
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static ControlProfile Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProfileException("Not a valid profile file: it must be a JSON object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in root.EnumerateObject())
            fields[field.Name] = field.Value; // unknown fields are kept here and ignored

        if (!fields.TryGetValue("format", out var format) || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out int version) || version < 1)
            throw new ProfileException("Missing \"format\"");
        if (version > Format)
            throw new ProfileException("This profile was made for a newer CouchLink");

        string name = fields.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
        if (name.Length is 0 or > MaxNameLength)
            throw new ProfileException($"\"name\" must be 1-{MaxNameLength} characters");

        string? game = null;
        if (fields.TryGetValue("game", out var g) && g.ValueKind != JsonValueKind.Null)
        {
            game = g.ValueKind == JsonValueKind.String ? g.GetString()!.Trim() : null;
            if (game is null || game.Length > MaxGameLength)
                throw new ProfileException($"\"game\" must be up to {MaxGameLength} characters");
            if (game.Length == 0)
                game = null;
        }

        int step = ControlSettings.DefaultStep;
        if (fields.TryGetValue("sensitivity", out var s)
            && (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out step)
                || step is < ControlSettings.MinStep or > ControlSettings.MaxStep))
            throw new ProfileException($"\"sensitivity\" must be {ControlSettings.MinStep}-{ControlSettings.MaxStep}");

        bool invertY = false;
        if (fields.TryGetValue("invertY", out var invert))
        {
            if (invert.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ProfileException("\"invertY\" must be true or false");
            invertY = invert.GetBoolean();
        }

        if (!fields.TryGetValue("controls", out var controls) || controls.ValueKind != JsonValueKind.Object)
            throw new ProfileException("Missing \"controls\"");

        var keys = new Dictionary<PadControl, IReadOnlyList<ushort>>();
        var labels = new Dictionary<PadControl, string>();
        var owners = new Dictionary<ushort, PadControl>();
        var seen = new HashSet<PadControl>();
        foreach (var entry in controls.EnumerateObject())
        {
            if (!ControlNames.TryGetValue(entry.Name, out var control))
                throw new ProfileException($"Unknown control \"{entry.Name}\"");
            if (!seen.Add(control))
                throw new ProfileException($"Control \"{control}\" is listed twice");
            if (entry.Value.ValueKind != JsonValueKind.Object)
                throw new ProfileException($"\"{control}\": must be an object with \"keys\" and \"label\"");
            foreach (var field in entry.Value.EnumerateObject())
            {
                if (field.Name.Equals("keys", StringComparison.OrdinalIgnoreCase))
                    ReadKeys(control, field.Value, keys, owners);
                else if (field.Name.Equals("label", StringComparison.OrdinalIgnoreCase))
                    ReadLabel(control, field.Value, labels);
                else
                    throw new ProfileException($"\"{control}\": unknown field \"{field.Name}\"");
            }
        }
        return new ControlProfile(name, game, step, invertY, keys, labels);
    }

    private static void ReadKeys(PadControl control, JsonElement value,
        Dictionary<PadControl, IReadOnlyList<ushort>> keys, Dictionary<ushort, PadControl> owners)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;
        string notAList = $"\"{control}\": \"keys\" must be a list of key names";
        if (value.ValueKind != JsonValueKind.Array)
            throw new ProfileException(notAList);
        var list = new List<ushort>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ProfileException(notAList);
            string id = item.GetString()!;
            if (!ProfileKeys.TryParse(id, out ushort vk))
                throw new ProfileException($"\"{control}\": unknown key \"{id}\"");
            if (KeyLayout.IsReserved(vk))
                throw new ProfileException($"\"{control}\": {KeyNames.Of(vk)} is reserved");
            if (list.Contains(vk))
                continue;
            if (owners.TryGetValue(vk, out var other))
                throw new ProfileException($"\"{ProfileKeys.IdOf(vk)}\" is on both {other} and {control}");
            owners[vk] = control;
            list.Add(vk);
        }
        if (list.Count > 0)
            keys[control] = list.ToArray();
    }

    private static void ReadLabel(PadControl control, JsonElement value, Dictionary<PadControl, string> labels)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;
        string? raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (raw is null || raw.Contains('\n') || raw.Contains('\r') || raw.Trim().Length > MaxLabelLength)
            throw new ProfileException($"\"{control}\": label must be up to {MaxLabelLength} characters on one line");
        string label = raw.Trim();
        if (label.Length > 0)
            labels[control] = label;
    }

    /// <summary>The parser's message without its "Path: ... | LineNumber: ..." tail (the line is shown separately).</summary>
    private static string ParserMessage(JsonException e)
    {
        string message = e.Message;
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut < 0)
            cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return (cut < 0 ? message : message[..cut]).TrimEnd(' ', '|', '.');
    }

    private sealed class ProfileException(string message) : Exception(message);
}
