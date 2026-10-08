namespace CouchLink.Core.Input;

/// <summary>
/// The player's controls for this run of the app: key layout, mouse sensitivity step, Invert Y, action
/// labels and the loaded profile's name. In memory only (spec 6.7): a profile is loaded from a file by
/// choice, nothing is saved by itself. Edit through this class so <see cref="Changed"/> is raised.
/// Thread-safe: the editor writes on the UI thread, the input loop and the F1 panel read on their own threads.
/// </summary>
public sealed class ControlSettings
{
    public const int MinStep = 1, MaxStep = 10, DefaultStep = 5;
    private const double StepFactor = 1.3;
    private static readonly IReadOnlyDictionary<PadControl, string> NoLabels = new Dictionary<PadControl, string>();

    private int _step = DefaultStep;
    private volatile bool _invertY;
    private IReadOnlyDictionary<PadControl, string> _labels = NoLabels; // replaced whole, read without a lock
    private volatile string? _profileName;
    private volatile string? _profileGame;
    private volatile bool _profileChanged;

    public KeyLayout Layout { get; } = KeyLayout.CreateDefault();

    public int SensitivityStep => Volatile.Read(ref _step);

    /// <summary>Stick deflection per mouse count for the current step.</summary>
    public double Sensitivity => SensitivityFor(SensitivityStep);

    public bool InvertY => _invertY;

    /// <summary>The loaded profile's name, or null on the built-in layout.</summary>
    public string? ProfileName => _profileName;

    /// <summary>The loaded profile's game, if it names one.</summary>
    public string? ProfileGame => _profileGame;

    /// <summary>True after an edit made since the profile was loaded. The editor and F1 show "(changed)".</summary>
    public bool ProfileChanged => _profileChanged;

    /// <summary>Raised after every edit, on the thread that made it.</summary>
    public event Action? Changed;

    public static double SensitivityFor(int step) =>
        MouseStick.DefaultSensitivity * Math.Pow(StepFactor, Math.Clamp(step, MinStep, MaxStep) - DefaultStep);

    /// <summary>What the control does in the game ("Shoot"), or null.</summary>
    public string? LabelFor(PadControl control) => Volatile.Read(ref _labels).GetValueOrDefault(control);

    public BindResult Bind(PadControl control, ushort key)
    {
        var result = Layout.Bind(control, key);
        if (result.Bound)
            Edited();
        return result;
    }

    public void SetSensitivityStep(int step)
    {
        step = Math.Clamp(step, MinStep, MaxStep);
        if (Interlocked.Exchange(ref _step, step) != step)
            Edited();
    }

    public void SetInvertY(bool invert)
    {
        if (_invertY == invert)
            return;
        _invertY = invert;
        Edited();
    }

    /// <summary>Sets what the control does in the game: one line, trimmed, at most 24 characters; empty removes it.</summary>
    public void SetLabel(PadControl control, string? label)
    {
        string text = (label ?? "").ReplaceLineEndings(" ").Trim();
        if (text.Length > ProfileFile.MaxLabelLength)
            text = text[..ProfileFile.MaxLabelLength].TrimEnd();
        if (text == (LabelFor(control) ?? ""))
            return;
        var next = new Dictionary<PadControl, string>(Volatile.Read(ref _labels));
        if (text.Length == 0)
            next.Remove(control);
        else
            next[control] = text;
        Volatile.Write(ref _labels, next);
        Edited();
    }

    /// <summary>Loads a whole profile: keys, sensitivity, Invert Y and labels, then one <see cref="Changed"/>.</summary>
    public void Apply(ControlProfile profile)
    {
        Layout.Replace(profile.Keys);
        Volatile.Write(ref _step, Math.Clamp(profile.SensitivityStep, MinStep, MaxStep));
        _invertY = profile.InvertY;
        Volatile.Write(ref _labels, new Dictionary<PadControl, string>(profile.Labels));
        _profileName = profile.Name;
        _profileGame = profile.Game;
        _profileChanged = false;
        Changed?.Invoke();
    }

    /// <summary>The current controls as a profile, for Save as….</summary>
    public ControlProfile ToProfile(string name, string? game) => new(
        name.Trim(),
        string.IsNullOrWhiteSpace(game) ? null : game.Trim(),
        SensitivityStep,
        InvertY,
        Layout.Current.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value),
        new Dictionary<PadControl, string>(Volatile.Read(ref _labels)));

    public void ResetToDefault()
    {
        Layout.ResetToDefault();
        Volatile.Write(ref _step, DefaultStep);
        _invertY = false;
        Volatile.Write(ref _labels, NoLabels);
        _profileName = null;
        _profileGame = null;
        _profileChanged = false;
        Changed?.Invoke();
    }

    private void Edited()
    {
        if (_profileName is not null)
            _profileChanged = true;
        Changed?.Invoke();
    }
}
