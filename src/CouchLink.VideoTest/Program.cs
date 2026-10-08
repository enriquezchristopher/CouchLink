using System.Diagnostics;
using CouchLink.Core.Video;
using CouchLink.Video;

// VideoTest: checks host capture and encoding on this PC's GPU without a client.
//   VideoTest capture [seconds]   counts screen changes and lost captures
if (args.Length == 0 || args[0] is not ("capture" or "encode" or "play"))
{
    Console.WriteLine("Usage: VideoTest capture [seconds] | VideoTest encode [seconds] [--resolution=1080p] [--fps=60] [--quality=balanced] [--encoder=h264_amf] [--out=videotest.h264] [--normal-priority]");
    Console.WriteLine("       VideoTest play <file.h264> [more files] [--software] [--windowed] [--fps=60]");
    return 2;
}

if (args[0] == "play")
    return Play(args);

int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 5;
return args[0] == "capture" ? Capture(seconds) : Encode(seconds, args);

static int Capture(int seconds)
{
    using var capture = DesktopCapture.Open();
    Console.WriteLine($"Adapter: {capture.AdapterName} (vendor 0x{capture.VendorId:X4})");
    Console.WriteLine($"Screen: {capture.Width}x{capture.Height} at {capture.RefreshRate} Hz (DisplayInfo says {DisplayInfo.PrimaryRefreshRate()} Hz)");

    int changed = 0, unchanged = 0, lost = 0;
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        switch (capture.TryCapture(TimeSpan.FromMilliseconds(16)))
        {
            case CaptureStatus.NewFrame: changed++; break;
            case CaptureStatus.NoChange: unchanged++; break;
            default: lost++; Thread.Sleep(16); break;
        }
    }
    Console.WriteLine($"{seconds} s: {changed} screen changes, {unchanged} waits with no change, {lost} lost");
    bool ok = changed > 0 && lost == 0;
    Console.WriteLine(ok ? "PASS: capture works" : "FAIL: no screen changes captured, or capture was lost");
    return ok ? 0 : 1;
}


static int Play(string[] args)
{
    var files = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
    if (files.Count == 0 || !FfmpegLibrary.TryLoad(out var error) && Fail(error))
        return 2;
    int fps = int.Parse(args.FirstOrDefault(a => a.StartsWith("--fps=", StringComparison.Ordinal))?[6..] ?? "60");
    var options = new PlayerOptions(Windowed: args.Contains("--windowed"), PreferHardware: !args.Contains("--software"));

    int decodeFailures = 0;
    bool closed = false;
    using var player = new VideoPlayer(options, () => default, () => decodeFailures++, () => closed = true, Console.WriteLine);
    Console.WriteLine($"Decoder: {player.DecoderName}" + (player.HardwareDecodeError is { } e ? $" (hardware unavailable: {e})" : ""));

    long total = 0;
    var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
    var clock = Stopwatch.StartNew();
    uint number = 0;
    foreach (var file in files)
    {
        var frames = H264Frames.Split(File.ReadAllBytes(file));
        Console.WriteLine($"{file}: {frames.Count} frames");
        for (int i = 0; i < frames.Count && !closed; i++, number++, total++)
        {
            player.Enqueue(new AssembledFrame(number, i == 0, frames[i]));
            var due = interval * (total + 1);
            while (clock.Elapsed < due)
                Thread.Sleep(1);
        }
    }
    Thread.Sleep(500); // let the last frames show
    Console.WriteLine($"Shown {player.FramesShown} of {total} frames with {player.DecoderName}, " +
        $"client delay {player.ClientDelay.TotalMilliseconds:0.0} ms, {decodeFailures} decode failures");

    bool ok = player.FramesShown >= total * 0.9 && decodeFailures == 0;
    Console.WriteLine(ok ? "PASS" : "FAIL: frames were dropped or failed to decode");
    return ok ? 0 : 1;

    static bool Fail(string? message)
    {
        Console.WriteLine($"FAIL: {message}");
        return true;
    }
}

static int Encode(int seconds, string[] args)
{
    string Option(string name, string fallback) =>
        args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..] ?? fallback;

    var resolution = StreamSettings.Resolutions.First(r =>
        string.Equals(StreamSettings.Label(r), Option("resolution", "1080p"), StringComparison.OrdinalIgnoreCase));
    var quality = StreamQualities.All.First(q =>
        string.Equals(StreamQualities.Label(q), Option("quality", "Balanced"), StringComparison.OrdinalIgnoreCase));
    var settings = new StreamSettings(resolution, int.Parse(Option("fps", "60")), quality);
    string outPath = Option("out", "videotest.h264");

    if (!FfmpegLibrary.TryLoad(out var error))
    {
        Console.WriteLine($"FAIL: {error}");
        return 1;
    }

    var capture = DesktopCapture.Open();
    if (!args.Contains("--normal-priority"))
        Console.WriteLine(GpuPriority.Raise(capture));
    IReadOnlyList<string> encoders = args.Any(a => a.StartsWith("--encoder=", StringComparison.Ordinal))
        ? [Option("encoder", EncoderChoice.Software)]
        : EncoderChoice.Candidates(capture.VendorId, settings.Quality);
    var encodeTimes = new List<double>();
    using var source = new ScreenVideoSource(capture, encoders,
        (name, size) => new TimedEncoder(new H264Encoder(capture, name, size, settings.FrameRate, settings.BitRateFor(size)), encodeTimes),
        settings, TimeProvider.System);

    Console.WriteLine($"Adapter: {capture.AdapterName} (vendor 0x{capture.VendorId:X4}), screen {capture.Width}x{capture.Height} at {capture.RefreshRate} Hz");
    foreach (var skipped in source.SkippedEncoders)
        Console.WriteLine($"Skipped {skipped}");
    Console.WriteLine($"Encoder: {source.EncoderName} ({(source.IsHardware ? "hardware" : "software")}), " +
        $"{source.Size.Width}x{source.Size.Height} at {settings.FrameRate} fps, {settings.BitRateFor(source.Size) / 1e6:0.0} Mbps target, " +
        $"{StreamQualities.Label(settings.Quality)} quality");

    using var file = File.Create(outPath);
    int frames = 0, keyframes = 0, paused = 0;
    long bytes = 0;
    var clock = Stopwatch.StartNew();
    bool forcedMiddle = false;
    while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        bool force = !forcedMiddle && clock.Elapsed > TimeSpan.FromSeconds(seconds / 2.0);
        if (!source.TryGetFrame(force, TimeSpan.FromMilliseconds(50), out var frame))
            continue;
        forcedMiddle |= force;
        file.Write(frame.Data.Span);
        frames++;
        bytes += frame.Data.Length;
        keyframes += frame.Keyframe ? 1 : 0;
        paused += frame.Paused ? 1 : 0;
    }

    encodeTimes.Sort();
    double P(double q) => encodeTimes.Count == 0 ? double.NaN : encodeTimes[(int)Math.Min(encodeTimes.Count - 1, q * encodeTimes.Count)];
    double budgetMs = 1000.0 / settings.FrameRate;
    Console.WriteLine($"{frames} frames in {seconds} s ({frames / (double)seconds:0.0} fps), {keyframes} keyframes, {paused} paused, " +
        $"{bytes * 8.0 / seconds / 1e6:0.0} Mbps; encode ms p50 {P(0.5):0.00} p95 {P(0.95):0.00} (budget {budgetMs:0.0})");
    Console.WriteLine($"Wrote {Path.GetFullPath(outPath)}");

    bool ok = frames >= 0.9 * seconds * settings.FrameRate && keyframes >= 2 && P(0.95) < budgetMs;
    Console.WriteLine(ok ? "PASS" : "FAIL: too few frames, a missing keyframe, or encoding slower than the frame rate");
    return ok ? 0 : 1;
}

sealed class TimedEncoder(IFrameEncoder inner, List<double> times) : IFrameEncoder
{
    public string Name => inner.Name;
    public bool IsHardware => inner.IsHardware;
    public VideoSize Size => inner.Size;

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        long start = Stopwatch.GetTimestamp();
        bool produced = inner.Encode(forceKeyframe, out frame);
        times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return produced;
    }

    public void Dispose() => inner.Dispose();
}
