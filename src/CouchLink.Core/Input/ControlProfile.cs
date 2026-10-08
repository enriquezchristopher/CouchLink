namespace CouchLink.Core.Input;

/// <summary>
/// A controller profile: a whole key layout for a game, its mouse settings and optional action labels
/// ("Shoot" on Square). Already validated. <see cref="Keys"/> holds only controls that have a key;
/// <see cref="Labels"/> only non-empty labels. Made by <see cref="ProfileFile.Load"/> and
/// <see cref="ControlSettings.ToProfile"/>.
/// </summary>
public sealed record ControlProfile(
    string Name,
    string? Game,
    int SensitivityStep,
    bool InvertY,
    IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Keys,
    IReadOnlyDictionary<PadControl, string> Labels);
