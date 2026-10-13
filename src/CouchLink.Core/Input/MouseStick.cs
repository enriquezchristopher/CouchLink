namespace CouchLink.Core.Input;

/// <summary>
/// Turns mouse speed into a right-stick deflection: a steady move holds the stick steady, and the
/// stick centres once the mouse has been still for <see cref="WindowSeconds"/> (2K shot-stick flicks).
/// Not thread-safe; <see cref="InputMapper"/> calls it under its lock.
/// </summary>
public sealed class MouseStick
{
    /// <summary>Deflection per mouse count per millisecond, before the anti-deadzone.</summary>
    public const double DefaultSensitivity = 0.4;

    /// <summary>
    /// Smallest deflection any movement gives. Games ignore a stick tilted less than roughly 0.1-0.25,
    /// so without it slow aiming does nothing and then lurches.
    /// </summary>
    public const double DefaultAntiDeadzone = 0.2;

    // Speed is the counts of the last 24 ms: three reports of a 125 Hz mouse, so it holds steady
    // between reports. A 6 ms smoothing evens out reports landing either side of the window's edge.
    private const double WindowSeconds = 0.024;
    private const double SmoothingSeconds = 0.006;

    // Each tick's counts with the time they were moved over: from the tick before to this one.
    private readonly Queue<(double Start, double End, double X, double Y)> _window = new();
    private double _now;
    private double _pendingX, _pendingY;
    private double _speedX, _speedY; // counts per millisecond, smoothed

    /// <summary>Stick deflection per mouse count per millisecond (1.0 = full).</summary>
    public double Sensitivity { get; set; } = DefaultSensitivity;

    public double AntiDeadzone { get; set; } = DefaultAntiDeadzone;

    /// <summary>Mouse toward you pushes the stick up instead of down.</summary>
    public bool InvertY { get; set; }

    public void AddDelta(int dx, int dy)
    {
        _pendingX += dx;
        _pendingY += InvertY ? -(double)dy : dy;
    }

    /// <summary>Advances time by <paramref name="dtSeconds"/>, takes in the movement since the last call and returns the stick.</summary>
    public (byte X, byte Y) Update(double dtSeconds)
    {
        double dt = Math.Max(dtSeconds, 0);
        _now += dt;
        if (_pendingX != 0 || _pendingY != 0)
        {
            _window.Enqueue((_now - dt, _now, _pendingX, _pendingY));
            _pendingX = _pendingY = 0;
        }
        double windowStart = _now - WindowSeconds;
        while (_window.Count > 0 && _window.Peek().End <= windowStart)
            _window.Dequeue();
        if (_window.Count == 0)
        {
            // Still for a whole window: centre now rather than let the smoothing trail off,
            // which the anti-deadzone would hold past the game's deadzone.
            Reset();
            return (PadState.Center, PadState.Center);
        }

        // Only the part of each tick inside the window counts. After the input loop stalls, a tick holds
        // all the movement of the stall; counted whole, steady aiming would land at once and read as a flick.
        double sumX = 0, sumY = 0;
        foreach (var (start, end, x, y) in _window)
        {
            double share = start >= windowStart || end <= start ? 1 : (end - windowStart) / (end - start);
            sumX += x * share;
            sumY += y * share;
        }

        double windowMs = WindowSeconds * 1000;
        double k = 1 - Math.Exp(-dt / SmoothingSeconds);
        _speedX += (sumX / windowMs - _speedX) * k;
        _speedY += (sumY / windowMs - _speedY) * k;

        double stickX = _speedX * Sensitivity, stickY = _speedY * Sensitivity;
        double magnitude = Math.Sqrt(stickX * stickX + stickY * stickY);
        if (magnitude == 0)
            return (PadState.Center, PadState.Center);
        double scaled = AntiDeadzone + (1 - AntiDeadzone) * Math.Min(magnitude, 1.0);
        return (StickMath.ToAxis(stickX / magnitude * scaled), StickMath.ToAxis(stickY / magnitude * scaled));
    }

    public void Reset()
    {
        _window.Clear();
        _pendingX = _pendingY = 0;
        _speedX = _speedY = 0;
    }
}
