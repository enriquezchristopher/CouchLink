using System.Runtime.InteropServices;
using CouchLink.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CouchLink.Audio;

/// <summary>Fills <paramref name="output"/> with interleaved 16-bit stereo PCM. <c>AudioClient.Read</c> fits.</summary>
public delegate int PcmReader(Span<short> output);

/// <summary>
/// Plays the client's audio on the Windows default output device in shared mode, with the shortest
/// buffer the driver allows (IAudioClient3 low latency, 2-3 ms; otherwise 10 ms). Low-latency mode
/// can't use Windows' automatic stream routing, so this listens for default-device changes itself
/// and reopens on the new device. With no device, or after the device stops (unplugged), it retries
/// every <see cref="RetryInterval"/>. PCM is pulled from <c>read</c> on the device's thread.
/// </summary>
public sealed class DefaultDevicePlayer : IDisposable
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private static readonly WaveFormat Format = new(AudioFormat.SampleRate, 16, AudioFormat.Channels);

    private readonly PullProvider _provider;
    private readonly Action<string>? _log;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly Lock _lock = new();
    private WasapiPlayer? _player;
    private Timer? _retry;
    private bool _disposed;
    private string _status = "starting";

    public DefaultDevicePlayer(PcmReader read, Action<string>? log = null)
    {
        _provider = new PullProvider(read);
        _log = log;
        _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
        Reopen();
    }

    /// <summary>The device and its buffer, e.g. "Headphones, 2 ms (low latency)", or why nothing plays.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (value == _status)
                return;
            _status = value;
            _log?.Invoke($"Audio output: {value}"); // only changes, so a missing device doesn't log every second
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
            ScheduleReopen(TimeSpan.Zero); // never reopen inside Windows' notification callback
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _log?.Invoke($"Audio output stopped{(e.Exception is { } ex ? $" ({ex.Message})" : "")}; reopening");
        ScheduleReopen(RetryInterval);
    }

    private void ScheduleReopen(TimeSpan delay)
    {
        lock (_lock)
        {
            if (_disposed || _retry is not null)
                return;
            _retry = new Timer(_ => Reopen(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reopen()
    {
        WasapiPlayer? old;
        lock (_lock)
        {
            _retry?.Dispose();
            _retry = null;
            if (_disposed)
                return;
            old = _player;
            _player = null;
        }
        if (old is not null)
        {
            old.PlaybackStopped -= OnStopped;
            old.Dispose();
        }

        try
        {
            var player = Open();
            lock (_lock)
            {
                if (_disposed)
                {
                    player.PlaybackStopped -= OnStopped;
                    player.Dispose();
                    return;
                }
                _player = player;
            }
            Status = $"{player.DeviceFriendlyName}, {player.LatencyMilliseconds} ms{(player.LowLatencyActive ? " (low latency)" : "")}";
        }
        catch (Exception e)
        {
            Status = _devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                ? $"unavailable: {e.Message}"
                : "no output device";
            ScheduleReopen(RetryInterval);
        }
    }

    private WasapiPlayer Open()
    {
        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        WasapiPlayer player;
        try
        {
            player = Build(device, lowLatency: true);
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio output: low-latency mode failed ({e.Message}); using a 10 ms buffer");
            player = Build(device, lowLatency: false);
        }

        try
        {
            player.Init(_provider);
            player.PlaybackStopped += OnStopped;
            player.Play();
            return player;
        }
        catch
        {
            player.PlaybackStopped -= OnStopped;
            player.Dispose();
            throw;
        }
    }

    private static WasapiPlayer Build(MMDevice device, bool lowLatency)
    {
        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithCategory(AudioStreamCategory.GameMedia);
        builder = lowLatency ? builder.WithLowLatency(true) : builder.WithLatency(10);
        return Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult(); // off the calling (UI) thread
    }

    public void Dispose()
    {
        WasapiPlayer? player;
        lock (_lock)
        {
            _disposed = true;
            _retry?.Dispose();
            _retry = null;
            player = _player;
            _player = null;
        }
        _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        _notifications.Dispose();
        if (player is not null)
        {
            player.PlaybackStopped -= OnStopped;
            player.Dispose();
        }
        _devices.Dispose();
    }

    /// <summary>NAudio's side of <see cref="PcmReader"/>: bytes asked for, 16-bit stereo given.</summary>
    private sealed class PullProvider(PcmReader read) : IWaveProvider
    {
        public WaveFormat WaveFormat => Format;

        public int Read(Span<byte> buffer)
        {
            int whole = buffer.Length / Format.BlockAlign * Format.BlockAlign;
            read(MemoryMarshal.Cast<byte, short>(buffer[..whole]));
            buffer[whole..].Clear();
            return buffer.Length;
        }
    }
}
