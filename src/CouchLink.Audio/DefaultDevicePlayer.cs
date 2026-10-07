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
/// every <see cref="RetryInterval"/>. Every open runs on one <see cref="ReopenLoop"/>, so two
/// changes in a row never leave two players pulling at once, and nothing it does can throw out of
/// a timer thread. It never throws: without a working audio system it only reports why in
/// <see cref="Status"/>. PCM is pulled from <c>read</c> on the device's thread.
/// </summary>
public sealed class DefaultDevicePlayer : IDisposable
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private static readonly WaveFormat Format = new(AudioFormat.SampleRate, 16, AudioFormat.Channels);

    private readonly PullProvider _provider;
    private readonly Action<string>? _log;
    private readonly MMDeviceEnumerator? _devices;
    private readonly MMDeviceNotificationClient? _notifications;
    private readonly ReopenLoop? _reopen;
    private WasapiPlayer? _player; // only the reopen loop's single attempt touches it, and Dispose after the loop stops
    private string _status = "starting";

    public DefaultDevicePlayer(PcmReader read, Action<string>? log = null)
        : this(read, log, () => new MMDeviceEnumerator())
    {
    }

    internal DefaultDevicePlayer(PcmReader read, Action<string>? log, Func<MMDeviceEnumerator> openDevices)
    {
        _provider = new PullProvider(read);
        _log = log;
        MMDeviceEnumerator? devices = null;
        try
        {
            devices = openDevices();
            _notifications = devices.CreateNotificationClient(useSynchronizationContext: false);
            _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
        }
        catch (Exception e)
        {
            devices?.Dispose(); // e.g. the Windows Audio service is stopped
            Status = $"unavailable: {e.Message}";
            return;
        }
        _devices = devices;
        _reopen = new ReopenLoop(TryOpen, RetryInterval, TimeProvider.System,
            e => Status = $"unavailable: {e.Message}");
        _reopen.Request(TimeSpan.Zero);
    }

    /// <summary>The device and its buffer, e.g. "Headphones, 2 ms (low latency)", or why nothing plays.</summary>
    public string Status
    {
        get => Volatile.Read(ref _status);
        private set
        {
            if (value == _status)
                return;
            Volatile.Write(ref _status, value);
            _log?.Invoke($"Audio output: {value}"); // only changes, so a missing device doesn't log every second
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
            _reopen?.Request(TimeSpan.Zero); // never reopen inside Windows' notification callback
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _log?.Invoke($"Audio output stopped{(e.Exception is { } ex ? $" ({ex.Message})" : "")}; reopening");
        _reopen?.Request(RetryInterval);
    }

    /// <summary>One attempt, on the reopen loop. Returns false (or throws) to try again later.</summary>
    private bool TryOpen()
    {
        CloseCurrent();
        if (!_devices!.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            Status = "no output device";
            return false;
        }

        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        WasapiPlayer player;
        try
        {
            player = Start(device, lowLatency: true);
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio output: low-latency mode failed ({e.Message}); using a 10 ms buffer");
            player = Start(device, lowLatency: false);
        }
        _player = player;
        Status = $"{player.DeviceFriendlyName}, {player.LatencyMilliseconds} ms{(player.LowLatencyActive ? " (low latency)" : "")}";
        return true;
    }

    private WasapiPlayer Start(MMDevice device, bool lowLatency)
    {
        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithCategory(AudioStreamCategory.GameMedia);
        builder = lowLatency ? builder.WithLowLatency(true) : builder.WithLatency(10);
        var player = Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult(); // off the calling thread
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

    private void CloseCurrent()
    {
        if (_player is not { } old)
            return;
        _player = null;
        old.PlaybackStopped -= OnStopped;
        try
        {
            old.Dispose();
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio output: closing the old device failed ({e.Message})"); // e.g. it was unplugged
        }
    }

    public void Dispose()
    {
        _reopen?.Dispose(); // waits for a running attempt; none start after this
        if (_notifications is not null)
        {
            _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _notifications.Dispose();
        }
        CloseCurrent();
        _devices?.Dispose();
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
