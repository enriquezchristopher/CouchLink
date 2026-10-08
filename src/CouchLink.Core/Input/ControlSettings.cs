namespace CouchLink.Core.Input;

/// <summary>
/// The player's controls for this run of the app: key layout, mouse sensitivity step and Invert Y.
/// In memory only (spec 6.7). Edit through this class so <see cref="Changed"/> is raised. Thread-safe:
/// the editor writes on the UI thread, the input loop and the F1 panel read on their own threads.
/// </summary>
public sealed class ControlSettings
{
    public const int MinStep = 1, MaxStep = 10, DefaultStep = 5;
    private const double StepFactor = 1.3;

    private int _step = DefaultStep;
    private volatile bool _invertY;

    public KeyLayout Layout { get; } = KeyLayout.CreateDefault();

    public int SensitivityStep => Volatile.Read(ref _step);

    /// <summary>Stick deflection per mouse count for the current step.</summary>
    public double Sensitivity => SensitivityFor(SensitivityStep);

    public bool InvertY => _invertY;

    /// <summary>Raised after every edit, on the thread that made it.</summary>
    public event Action? Changed;

    public static double SensitivityFor(int step) =>
        MouseStick.DefaultSensitivity * Math.Pow(StepFactor, Math.Clamp(step, MinStep, MaxStep) - DefaultStep);

    public BindResult Bind(PadControl control, ushort key)
    {
        var result = Layout.Bind(control, key);
        if (result.Bound)
            Changed?.Invoke();
        return result;
    }

    public void SetSensitivityStep(int step)
    {
        step = Math.Clamp(step, MinStep, MaxStep);
        if (Interlocked.Exchange(ref _step, step) != step)
            Changed?.Invoke();
    }

    public void SetInvertY(bool invert)
    {
        if (_invertY == invert)
            return;
        _invertY = invert;
        Changed?.Invoke();
    }

    public void ResetToDefault()
    {
        Layout.ResetToDefault();
        Volatile.Write(ref _step, DefaultStep);
        _invertY = false;
        Changed?.Invoke();
    }
}
