using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// The host's video: the screen through <see cref="ScreenVideoSource"/> with the chosen
/// settings, or the test pattern with --test-pattern. If video can't start (no FFmpeg, no
/// encoder, no display) hosting still runs the pads, and <see cref="Describe"/> says why.
/// </summary>
internal sealed class HostVideo : IDisposable
{
    private readonly VideoStreamer? _streamer;
    private readonly ScreenVideoSource? _screen;
    private readonly StreamSettings _settings;
    private readonly string _summary;

    private HostVideo(VideoStreamer? streamer, ScreenVideoSource? screen, StreamSettings settings, string summary)
    {
        _streamer = streamer;
        _screen = screen;
        _settings = settings;
        _summary = summary;
    }

    public static HostVideo Start(StreamSettings settings, Action<Exception> onError)
    {
        if (AppServices.Options.TestPattern)
        {
            var pattern = new TestPatternSource(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / settings.FrameRate));
            return new HostVideo(Stream(pattern, onError), null, settings, $"test pattern at {settings.FrameRate} fps");
        }

        if (!FfmpegLibrary.TryLoad(out var error))
            return Unavailable(settings, error!);

        DesktopCapture? capture = null;
        try
        {
            capture = DesktopCapture.Open();
            var priority = GpuPriority.Raise(capture);
            AppServices.Log.Write(priority);
            var screenCapture = capture;
            var screen = new ScreenVideoSource(screenCapture, EncoderChoice.Candidates(capture.VendorId, settings.Quality),
                (name, size) => new H264Encoder(screenCapture, name, size, settings.FrameRate, settings.BitRateFor(size)),
                settings, TimeProvider.System);
            foreach (var skipped in screen.SkippedEncoders)
                AppServices.Log.Write($"Video encoder skipped: {skipped}");
            var summary = $"{screen.EncoderName} {screen.Size.Width}x{screen.Size.Height} at {settings.FrameRate} fps, " +
                $"{settings.BitRateFor(screen.Size) / 1e6:0.0} Mbps, {StreamQualities.Label(settings.Quality)} quality " +
                $"({(screen.IsHardware ? "hardware" : "software")}) on {capture.AdapterName}";
            AppServices.Log.Write($"Video: {summary}");
            return new HostVideo(Stream(screen, onError), screen, settings, $"{summary}\n  {priority}");
        }
        catch (Exception e)
        {
            capture?.Dispose();
            return Unavailable(settings, e.Message);
        }
    }

    private static VideoStreamer Stream(IEncodedVideoSource source, Action<Exception> onError) =>
        new(source, new VideoSender(), Ports.Video, TimeProvider.System, onError);

    private static HostVideo Unavailable(StreamSettings settings, string reason)
    {
        AppServices.Log.Write($"Video unavailable: {reason}");
        return new HostVideo(null, null, settings, $"unavailable: {reason}");
    }

    public void AddTarget(byte slot, IPAddress address) => _streamer?.AddTarget(slot, address);

    public void RemoveTarget(byte slot) => _streamer?.RemoveTarget(slot);

    public void RequestKeyframe() => _streamer?.RequestKeyframe();

    public void ReplyToTimingPing(TimingPing ping, IPAddress from) => _streamer?.ReplyToTimingPing(ping, from);

    /// <summary>The screen stream's bitrate per client; null for the test pattern or when video is unavailable.</summary>
    public long? BitRate => _screen is { } screen ? _settings.BitRateFor(screen.Size) : null;

    public string Describe()
    {
        var text = $"Video: {_summary}";
        if (_streamer is { } streamer)
        {
            var s = streamer.Stats;
            text += $"\n  {s.Clients} client(s), {s.FramesSent} frames, {s.KeyframesSent} keyframes, {s.BytesSent / 1_000_000.0:0.0} MB";
        }
        if (_screen is { HasEncoder: false })
            text += $"\n  No video encoder after the screen changed; retrying ({string.Join(" ", _screen.SkippedEncoders)})";
        else if (_screen is { IsHardware: false })
            text += $"\n  {EncoderChoice.SoftwareWarning}";
        if (_screen is { Paused: true })
            text += "\n  Host screen paused (capture lost)";
        return text;
    }

    public void Dispose() => _streamer?.Dispose(); // the streamer owns the source, which owns the capture
}
