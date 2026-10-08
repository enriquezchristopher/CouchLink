using CouchLink.Core.Video;

namespace CouchLink.Video;

/// <summary>
/// The player's logic, run on the player thread one pass at a time. Every queued frame is decoded in
/// order (H.264 needs them all) but only the newest picture is presented. A hardware decode error
/// switches to software; any decode error drops the rest and resyncs on the next keyframe; a backlog
/// longer than <see cref="MaxBacklog"/> is skipped to its last keyframe, or dropped and resynced.
/// Without new frames it redraws when the status or stats text changes, and every
/// <see cref="RedrawInterval"/>.
/// </summary>
public sealed class PlayerCore : IDisposable
{
    public const int MaxBacklog = 6;
    public static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>No frame for this long: the host has gone quiet (stopped, gone, or a wrong address).</summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromSeconds(2);

    /// <summary>How long "F1: controls · Ctrl+Alt+Q: leave" shows after the first picture of a join.</summary>
    public static readonly TimeSpan StartHintFor = TimeSpan.FromSeconds(5);

    private readonly Func<bool, IFrameDecoder> _openDecoder;
    private readonly IFramePresenter _presenter;
    private readonly Func<VideoClientStats> _stats;
    private readonly Action _decodeFailed;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly Func<string?>? _audioLine;
    private readonly Func<string?>? _sessionStatus;
    private readonly Func<string?>? _controlsText;
    private readonly long _start;
    private readonly Lock _queueLock = new();
    private readonly List<(AssembledFrame Frame, TimeSpan ReceivedAt)> _queue = [];
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly StatsWindow _statsWindow = new();
    private IFrameDecoder _decoder;
    private DecodedPicture? _last;
    private StatsSample? _sample;
    private TimeSpan? _lastPresent;
    private TimeSpan? _lastArrival;
    private string? _lastStatus, _lastStatsText;
    private long _clientDelayTicks;
    private TimeSpan? _firstFrameAt;
    private bool _showControls, _hintDismissed;
    private string? _lastControls, _lastHint;

    public PlayerCore(Func<bool, IFrameDecoder> openDecoder, IFramePresenter presenter,
        Func<VideoClientStats> stats, Action decodeFailed, TimeProvider time, Action<string>? log = null,
        Func<string?>? audioLine = null, Func<string?>? sessionStatus = null, Func<string?>? controlsText = null)
    {
        _openDecoder = openDecoder;
        _presenter = presenter;
        _stats = stats;
        _decodeFailed = decodeFailed;
        _time = time;
        _log = log;
        _audioLine = audioLine;
        _sessionStatus = sessionStatus;
        _controlsText = controlsText;
        _start = time.GetTimestamp();
        _decoder = openDecoder(true);
    }

    public WaitHandle FrameReady => _frameReady;
    public bool ShowStats { get; set; }

    /// <summary>The F1 panel. Turning it on also dismisses the start hint for good.</summary>
    public bool ShowControls
    {
        get => _showControls;
        set
        {
            _showControls = value;
            if (value)
                _hintDismissed = true;
        }
    }
    public long FramesShown { get; private set; }
    public string DecoderName => _decoder.Name;
    public TimeSpan ClientDelay => TimeSpan.FromTicks(Interlocked.Read(ref _clientDelayTicks));

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>Queues a frame for the player thread. Any thread.</summary>
    public void Enqueue(AssembledFrame frame)
    {
        lock (_queueLock)
            _queue.Add((frame, Now));
        _frameReady.Set();
    }

    public void Run()
    {
        List<(AssembledFrame Frame, TimeSpan ReceivedAt)> batch;
        lock (_queueLock)
        {
            batch = [.. _queue];
            _queue.Clear();
        }
        if (batch.Count > 0)
            _lastArrival = batch[^1].ReceivedAt;

        if (batch.Count > MaxBacklog)
        {
            int key = batch.FindLastIndex(f => f.Frame.Keyframe);
            if (key >= 0)
            {
                batch = batch[key..];
            }
            else
            {
                _log?.Invoke($"Video player fell {batch.Count} frames behind; waiting for a keyframe");
                batch.Clear();
                _decodeFailed();
            }
        }

        (DecodedPicture Picture, AssembledFrame Frame, TimeSpan ReceivedAt)? newest = null;
        foreach (var (frame, receivedAt) in batch)
        {
            try
            {
                bool wasHardware = _decoder.IsHardware;
                if (_decoder.Decode(frame.Data, out var picture))
                    newest = (picture, frame, receivedAt);
                if (wasHardware && !_decoder.IsHardware)
                {
                    // FFmpeg gave up on the GPU by itself, with a decoder set up for the GPU
                    // (one thread); a proper software decoder is much faster.
                    SwitchToSoftware("FFmpeg could not decode this stream on the GPU");
                    newest = null;
                    break;
                }
            }
            catch (FfmpegException e)
            {
                OnDecodeError(e);
                newest = null; // later frames in this batch depend on the broken one
                break;
            }
        }

        var sinceLastFrame = _lastArrival is { } arrived ? Now - arrived : TimeSpan.Zero;
        string? status = OverlayText.Status(FramesShown > 0 || newest is not null, _stats().HostPaused, sinceLastFrame,
            QuietAfter, _sessionStatus?.Invoke());
        if (newest is { } n)
        {
            _last = n.Picture;
            FramesShown++;
            _firstFrameAt ??= Now;
        }
        UpdateStats();
        string? statsText = ShowStats ? OverlayText.Stats(_sample, DecoderName, _audioLine?.Invoke()) : null;
        string? controls = ShowControls ? _controlsText?.Invoke() : null;
        string? hint = !_hintDismissed && _firstFrameAt is { } first && Now - first < StartHintFor ? OverlayText.StartHint : null;

        bool due = newest is not null
            || _lastPresent is null
            || Now - _lastPresent >= RedrawInterval
            || status != _lastStatus
            || statsText != _lastStatsText
            || controls != _lastControls
            || hint != _lastHint;
        if (!due)
            return;

        _presenter.Present(_last, status, statsText, controls, hint);
        _lastPresent = Now;
        _lastStatus = status;
        _lastStatsText = statsText;
        _lastControls = controls;
        _lastHint = hint;
        if (newest is { } shown)
            RecordClientDelay(shown.Frame.AssemblyTime + (Now - shown.ReceivedAt));
    }

    private void OnDecodeError(FfmpegException e)
    {
        if (_decoder.IsHardware)
        {
            SwitchToSoftware($"Hardware video decoding failed ({e.Message})");
            return;
        }
        _log?.Invoke($"Video decoding failed ({e.Message}); waiting for a keyframe");
        _decodeFailed();
    }

    /// <summary>Replaces the decoder with a software one, which starts at the next keyframe.</summary>
    private void SwitchToSoftware(string reason)
    {
        _log?.Invoke($"{reason}; switching to software decoding");
        _decoder.Dispose();
        _last = null; // its picture belonged to the old decoder
        _decoder = _openDecoder(false);
        _decodeFailed();
    }

    private void UpdateStats()
    {
        if (_statsWindow.Update(_stats(), FramesShown, ClientDelay, Now) is { } sample)
            _sample = sample;
    }

    private void RecordClientDelay(TimeSpan delay)
    {
        long old = Interlocked.Read(ref _clientDelayTicks);
        Interlocked.Exchange(ref _clientDelayTicks, old == 0 ? delay.Ticks : old + (delay.Ticks - old) / 8);
    }

    public void Dispose()
    {
        _decoder.Dispose();
        _frameReady.Dispose();
    }
}
