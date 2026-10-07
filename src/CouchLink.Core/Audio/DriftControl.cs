namespace CouchLink.Core.Audio;

/// <summary>
/// Client side: holds the jitter buffer at its starting depth while the host's and the client's
/// sound cards run at slightly different rates. It is given the depth before each played frame.
/// The average over the first <see cref="WindowFrames"/> (1 s) is the baseline; it already
/// includes how the device pulls audio. When a later second averages more than
/// <see cref="DeadBandFrames"/> above it, one sample per frame is dropped until a second averages
/// at or below it; below it, one is repeated. That is a 0.4% speed change, too small to hear, and
/// moves the depth by one frame in about 1.2 s. Not thread-safe.
/// </summary>
public sealed class DriftControl
{
    public const int WindowFrames = 200;
    public const double DeadBandFrames = 1;

    private long _sum;
    private int _count;
    private int _correction; // -1 drop, +1 repeat, 0 none

    public double? Baseline { get; private set; }

    public long Corrections { get; private set; }

    /// <summary>Returns -1 to drop one sample from the next frame, +1 to repeat one, 0 to leave it.</summary>
    public int Next(int depthFrames)
    {
        _sum += depthFrames;
        if (++_count == WindowFrames)
        {
            double average = (double)_sum / WindowFrames;
            _sum = 0;
            _count = 0;
            if (Baseline is not { } baseline)
                Baseline = average;
            else if (average > baseline + DeadBandFrames)
                _correction = -1;
            else if (average < baseline - DeadBandFrames)
                _correction = +1;
            else if ((_correction == -1 && average <= baseline) || (_correction == +1 && average >= baseline))
                _correction = 0;
        }
        if (_correction != 0)
            Corrections++;
        return _correction;
    }

    /// <summary>Playback started again (after silence, or a new stream): learn a new baseline.</summary>
    public void Reset()
    {
        Baseline = null;
        _sum = 0;
        _count = 0;
        _correction = 0;
    }
}
