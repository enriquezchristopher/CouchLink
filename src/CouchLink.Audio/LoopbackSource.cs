using System.Collections.Concurrent;
using CouchLink.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CouchLink.Audio;

/// <summary>
/// The host's sound, captured with WASAPI loopback as 48 kHz 16-bit stereo (Windows resamples and
/// downmixes). On Windows 10 2004 and later it is process loopback, leaving out CouchLink's own
/// process tree; it delivers 10 ms buffers continuously, zeros included. Otherwise it is loopback of
/// the default output device, which delivers nothing while the PC is silent and flags the first
/// buffer after. A frame after missing audio (WASAPI's discontinuity flag, a jump in the device
/// position, a dropped backlog or a restart) is marked as a discontinuity. If capture stops it
/// reopens every <see cref="RetryInterval"/>.
/// </summary>
public sealed class LoopbackSource : IAudioSource
{
    public const string ProcessLoopback = "process loopback";
    public const string DeviceLoopback = "device loopback";
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    /// <summary>200 ms. If the streamer falls this far behind, the backlog is dropped.</summary>
    private const int MaxQueuedFrames = 40;

    private static readonly WaveFormat Format = new(AudioFormat.SampleRate, 16, AudioFormat.Channels);

    private readonly BlockingCollection<(short[] Frame, bool Discontinuity)> _frames = new();
    private readonly MMDeviceEnumerator _devices = new();
    private readonly FrameSlicer _slicer;
    private readonly Action<string>? _log;
    private readonly Lock _lock = new();
    private WasapiRecorder? _recorder;
    private Timer? _retry;
    private long _expectedPosition = -1;
    private bool _disposed;

    private LoopbackSource(Action<string>? log)
    {
        _log = log;
        _slicer = new FrameSlicer(Enqueue);
    }

    public string Description { get; private set; } = "";

    /// <summary>Starts capturing. Throws if the host's sound can't be captured at all.</summary>
    public static LoopbackSource Open(Action<string>? log = null)
    {
        var source = new LoopbackSource(log);
        try
        {
            source.Start();
        }
        catch
        {
            source.Dispose();
            throw;
        }
        return source;
    }

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        if (!_frames.TryTake(out var item, timeout))
            return false;
        item.Frame.CopyTo(frame);
        discontinuity = item.Discontinuity;
        return true;
    }

    private void Start()
    {
        var recorder = Build(out var description);
        recorder.DataAvailable += OnData;
        recorder.RecordingStopped += OnStopped;
        lock (_lock)
        {
            if (_disposed)
            {
                recorder.RecordingStopped -= OnStopped;
                recorder.Dispose();
                return;
            }
            _recorder = recorder;
            _expectedPosition = -1;
            _slicer.Write([], discontinuity: true);
            Description = description;
        }
        recorder.StartRecording();
    }

    private WasapiRecorder Build(out string description)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            try
            {
                var builder = new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)Environment.ProcessId, ProcessLoopbackMode.ExcludeTargetProcessTree)
                    .WithFormat(Format)
                    .WithEventSync();
                description = ProcessLoopback;
                // Off the calling (UI) thread: activation completes asynchronously.
                return Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _log?.Invoke($"Audio: process loopback unavailable ({e.Message}); using device loopback");
            }
        }

        // Device loopback can't use NAudio's automatic default-device routing, so take the default now.
        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var deviceBuilder = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().WithFormat(Format).WithEventSync();
        description = DeviceLoopback;
        return Task.Run(() => deviceBuilder.BuildAsync()).GetAwaiter().GetResult();
    }

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (data.IsEmpty)
            return; // device loopback hands out empty buffers while the PC is silent
        lock (_lock)
        {
            // Process loopback reports position 0 throughout; device loopback's position jumps after silence.
            bool jumped = devicePosition != 0 && _expectedPosition >= 0 && devicePosition != _expectedPosition;
            _expectedPosition = devicePosition + data.Length / Format.BlockAlign;
            _slicer.Write(data,
                discontinuity: jumped || flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity),
                silent: flags.HasFlag(AudioClientBufferFlags.Silent));
        }
    }

    /// <summary>From the slicer, under <c>_lock</c>.</summary>
    private void Enqueue(short[] frame, bool discontinuity)
    {
        if (_frames.Count >= MaxQueuedFrames)
        {
            while (_frames.TryTake(out _))
            {
            }
            discontinuity = true;
        }
        _frames.Add((frame, discontinuity));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _retry is not null)
                return;
            _log?.Invoke($"Audio capture stopped{(e.Exception is { } ex ? $" ({ex.Message})" : "")}; reopening");
            _retry = new Timer(_ => Reopen(), null, RetryInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reopen()
    {
        WasapiRecorder? old;
        lock (_lock)
        {
            _retry?.Dispose();
            _retry = null;
            if (_disposed)
                return;
            old = _recorder;
            _recorder = null;
        }
        if (old is not null)
        {
            old.RecordingStopped -= OnStopped;
            old.Dispose();
        }

        try
        {
            Start();
            _log?.Invoke($"Audio capture reopened ({Description})");
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio capture could not reopen ({e.Message}); retrying");
            lock (_lock)
                if (!_disposed)
                    _retry = new Timer(_ => Reopen(), null, RetryInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        WasapiRecorder? recorder;
        lock (_lock)
        {
            _disposed = true;
            _retry?.Dispose();
            _retry = null;
            recorder = _recorder;
            _recorder = null;
        }
        if (recorder is not null)
        {
            recorder.RecordingStopped -= OnStopped;
            recorder.StopRecording();
            recorder.Dispose();
        }
        _devices.Dispose();
        _frames.Dispose();
    }
}
