namespace CouchLink.Core.Input;

/// <summary>
/// Turns mouse movement into a right-stick deflection that springs back to
/// center shortly after the mouse stops (supports 2K shot-stick flicks).
/// </summary>
public sealed class MouseStick
{
    public const double DefaultSensitivity = 0.02;

    // exp(-t / 10 ms) drops a full deflection below SnapToZero in ~40 ms.
    private const double DecayTimeConstantSeconds = 0.010;
    private const double SnapToZero = 0.02;

    private double _x;
    private double _y;

    /// <summary>Stick deflection per mouse count (1.0 = full).</summary>
    public double Sensitivity { get; set; } = DefaultSensitivity;

    public void AddDelta(int dx, int dy)
    {
        _x += dx * Sensitivity;
        _y += dy * Sensitivity;
        double magnitude = Math.Sqrt(_x * _x + _y * _y);
        if (magnitude > 1.0)
        {
            _x /= magnitude;
            _y /= magnitude;
        }
    }

    /// <summary>Returns the current stick, then decays it by <paramref name="dtSeconds"/>.</summary>
    public (byte X, byte Y) Update(double dtSeconds)
    {
        var output = (StickMath.ToAxis(_x), StickMath.ToAxis(_y));
        double k = Math.Exp(-Math.Max(dtSeconds, 0) / DecayTimeConstantSeconds);
        _x *= k;
        _y *= k;
        if (Math.Abs(_x) < SnapToZero) _x = 0;
        if (Math.Abs(_y) < SnapToZero) _y = 0;
        return output;
    }

    public void Reset()
    {
        _x = 0;
        _y = 0;
    }
}
