using CouchLink.Core.Video;

namespace CouchLink.Video;

/// <summary>
/// The host screen as an <see cref="IEncodedVideoSource"/>. A changed screen is encoded at
/// once, at most <see cref="StreamSettings.FrameRate"/> times a second. A still screen is
/// re-encoded once per frame interval (tiny delta frames), so clients always get newer frames
/// and notice a lost one. While capture is lost the last image keeps going out marked Paused;
/// the first frame after it, and the first after the screen size changes, is a keyframe.
/// Owns the capture and the encoder. Used from the streamer's one thread.
/// </summary>
public sealed class ScreenVideoSource : IEncodedVideoSource
{
    private readonly IScreenCapture _capture;
    private readonly IReadOnlyList<string> _encoderNames;
    private readonly Func<string, VideoSize, IFrameEncoder> _openEncoder;
    private readonly StreamSettings _settings;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;
    private readonly long _start;
    private readonly TimeSpan _interval;
    private readonly List<string> _skipped = [];
    private IFrameEncoder _encoder;
    private (int Width, int Height) _encodedFrom;
    private TimeSpan? _lastSent;
    private bool _keyframeOwed;

    public ScreenVideoSource(
        IScreenCapture capture,
        IReadOnlyList<string> encoderNames,
        Func<string, VideoSize, IFrameEncoder> openEncoder,
        StreamSettings settings,
        TimeProvider time,
        Action<TimeSpan>? sleep = null)
    {
        _capture = capture;
        _encoderNames = encoderNames;
        _openEncoder = openEncoder;
        _settings = settings;
        _time = time;
        _sleep = sleep ?? Thread.Sleep;
        _start = time.GetTimestamp();
        _interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / settings.FrameRate);
        _encoder = OpenEncoder();
    }

    public string EncoderName => _encoder.Name;
    public bool IsHardware => _encoder.IsHardware;
    public VideoSize Size => _encoder.Size;
    public bool Paused { get; private set; }

    /// <summary>Encoders that failed to open before the current one, with the reason.</summary>
    public IReadOnlyList<string> SkippedEncoders => _skipped;

    private TimeSpan Now => _time.GetElapsedTime(_start);

    public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
    {
        frame = default;
        var due = _lastSent is { } last ? last + _interval : Now;
        var wait = Clamp(due - Now, timeout);

        var status = _capture.TryCapture(wait);
        if (status == CaptureStatus.Lost)
        {
            Paused = true;
            var remaining = due - Now; // capture fails fast: wait for the frame slot instead of spinning
            if (remaining > timeout)
            {
                _sleep(timeout);
                return false;
            }
            if (remaining > TimeSpan.Zero)
                _sleep(remaining);
        }
        else
        {
            if (Paused)
            {
                Paused = false;
                _keyframeOwed = true;
            }
            if (status == CaptureStatus.NoChange && Now < due)
                return false; // no change, and no repeat due yet
            if (status == CaptureStatus.NewFrame && Now < due)
                _sleep(due - Now); // the screen changes faster than the frame rate
        }

        if ((_capture.Width, _capture.Height) != _encodedFrom)
        {
            _encoder.Dispose();
            _encoder = OpenEncoder();
            _keyframeOwed = true;
        }

        if (!_encoder.Encode(forceKeyframe || _keyframeOwed, out var encoded))
            return false;
        if (encoded.Keyframe)
            _keyframeOwed = false;
        frame = encoded with { Paused = Paused };
        _lastSent = Now;
        return true;
    }

    private IFrameEncoder OpenEncoder()
    {
        _skipped.Clear();
        _encodedFrom = (_capture.Width, _capture.Height);
        var size = _settings.SizeFor(_capture.Width, _capture.Height);
        foreach (var name in _encoderNames)
        {
            try
            {
                return _openEncoder(name, size);
            }
            catch (Exception e)
            {
                _skipped.Add($"{name}: {e.Message}");
            }
        }
        throw new InvalidOperationException($"No video encoder could be opened. {string.Join(" ", _skipped)}");
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;

    public void Dispose()
    {
        _encoder.Dispose();
        _capture.Dispose();
    }
}
