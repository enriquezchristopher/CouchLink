using CouchLink.Core.Video;

namespace CouchLink.Video;

/// <summary>
/// The host screen as an <see cref="IEncodedVideoSource"/>. Frames go out on a fixed schedule of
/// <see cref="StreamSettings.FrameRate"/> a second, each with the newest screen image. A still screen is
/// re-encoded once per frame interval (tiny delta frames), so clients always get newer frames
/// and notice a lost one. While capture is lost the last image keeps going out marked Paused;
/// the first frame after it, and the first after the screen size changes, is a keyframe.
/// Owns the capture and the encoder. Used from the streamer's one thread.
/// </summary>
public sealed class ScreenVideoSource : IEncodedVideoSource
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly IScreenCapture _capture;
    private readonly IReadOnlyList<string> _encoderNames;
    private readonly Func<string, VideoSize, IFrameEncoder> _openEncoder;
    private readonly StreamSettings _settings;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;
    private readonly long _start;
    private readonly TimeSpan _interval;
    private readonly List<string> _skipped = [];
    private readonly TimerResolution? _timerResolution;
    private IFrameEncoder? _encoder;
    private (int Width, int Height) _encodedFrom;
    private TimeSpan? _lastSent;
    private bool _keyframeOwed;
    private TimeSpan _retryAt;
    private TimeSpan? _imageAt; // when the oldest unsent screen change was captured

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
        _encoder = TryOpenEncoder() ?? throw new InvalidOperationException(NoEncoderMessage);
        if (sleep is null)
            _timerResolution = new TimerResolution(); // real sleeps need 1 ms precision to keep the frame rate
    }

    public string EncoderName => _encoder?.Name ?? "none";
    public bool IsHardware => _encoder?.IsHardware ?? false;
    public VideoSize Size => _encoder?.Size ?? _settings.SizeFor(_capture.Width, _capture.Height);
    public bool Paused { get; private set; }

    /// <summary>False while an encoder failed to reopen after the screen changed; it is retried every second.</summary>
    public bool HasEncoder => _encoder is not null;

    /// <summary>Encoders that failed to open before the current one, with the reason.</summary>
    public IReadOnlyList<string> SkippedEncoders => _skipped;

    private TimeSpan Now => _time.GetElapsedTime(_start);

    public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
    {
        frame = default;
        if (_encoder is null && !TryReopenEncoder(timeout))
            return false;

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
            if (status == CaptureStatus.NewFrame)
                _imageAt ??= Now;
            if (Now < due)
                return false; // LastFrame holds the newest image; the slot sends whatever is newest then
        }

        if ((_capture.Width, _capture.Height) != _encodedFrom)
        {
            _encoder!.Dispose();
            _encoder = null;
            if (!TryReopenEncoder(timeout))
                return false;
        }

        var slot = Now;
        if (!_encoder!.Encode(forceKeyframe || _keyframeOwed, out var encoded))
            return false;
        if (encoded.Keyframe)
            _keyframeOwed = false;
        var imageAt = _imageAt is { } at && at <= slot ? at : slot; // a repeat is as old as its slot
        frame = encoded with { Paused = Paused, CaptureToEncoded = Now - imageAt };
        _imageAt = null;
        // Stay on the ideal schedule, so neither encoding time nor late waits slow the rate;
        // start a new one only when a whole interval behind, rather than burst to catch up.
        _lastSent = slot - due < _interval ? due : slot;
        return true;
    }

    /// <summary>Opens an encoder for the current screen size, at most once a <see cref="RetryDelay"/>.</summary>
    private bool TryReopenEncoder(TimeSpan timeout)
    {
        if (Now < _retryAt)
        {
            _sleep(Clamp(_retryAt - Now, timeout));
            return false;
        }
        _encoder = TryOpenEncoder();
        if (_encoder is null)
        {
            _retryAt = Now + RetryDelay;
            return false;
        }
        _keyframeOwed = true;
        return true;
    }

    private IFrameEncoder? TryOpenEncoder()
    {
        _skipped.Clear();
        var from = (_capture.Width, _capture.Height);
        var size = _settings.SizeFor(from.Width, from.Height);
        foreach (var name in _encoderNames)
        {
            try
            {
                var encoder = _openEncoder(name, size);
                _encodedFrom = from;
                return encoder;
            }
            catch (Exception e)
            {
                _skipped.Add($"{name}: {e.Message}");
            }
        }
        return null;
    }

    private string NoEncoderMessage => $"No video encoder could be opened. {string.Join(" ", _skipped)}";

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;

    public void Dispose()
    {
        _encoder?.Dispose();
        _capture.Dispose();
        _timerResolution?.Dispose();
    }
}
