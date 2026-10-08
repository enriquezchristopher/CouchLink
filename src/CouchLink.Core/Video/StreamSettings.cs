namespace CouchLink.Core.Video;

/// <summary>Stream resolution presets, by the stream's maximum height.</summary>
public enum StreamResolution
{
    Native = 0,
    P1080 = 1080,
    P900 = 900,
    P720 = 720,
    P540 = 540,
}

/// <summary>
/// What the host streams: a resolution preset, a frame rate and a quality, chosen in the host lobby.
/// The bitrate follows from them: 10 Mbps at 1080p60, scaled by pixels, frame rate and quality.
/// </summary>
public sealed record StreamSettings
{
    public const long BaseBitRate = 10_000_000;
    public const long MinBitRate = 2_000_000;
    public const long MaxBitRate = 100_000_000;

    public static IReadOnlyList<int> CommonFrameRates { get; } = [60, 75, 90, 120, 144, 165, 240];

    public static IReadOnlyList<StreamResolution> Resolutions { get; } =
        [StreamResolution.Native, StreamResolution.P1080, StreamResolution.P900, StreamResolution.P720, StreamResolution.P540];

    public static StreamSettings Default { get; } = new(StreamResolution.P1080, 60);

    public StreamSettings(StreamResolution resolution, int frameRate, StreamQuality quality = StreamQuality.Balanced)
    {
        if (!Resolutions.Contains(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution), $"Unknown resolution {resolution}.");
        if (!CommonFrameRates.Contains(frameRate))
            throw new ArgumentOutOfRangeException(nameof(frameRate), $"Frame rate must be one of {string.Join(", ", CommonFrameRates)}.");
        if (!StreamQualities.All.Contains(quality))
            throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}.");
        Resolution = resolution;
        FrameRate = frameRate;
        Quality = quality;
    }

    public StreamResolution Resolution { get; }
    public int FrameRate { get; }
    public StreamQuality Quality { get; }

    /// <summary>
    /// Rates the host can pick: up to the display's refresh rate, and always 60. Windows truncates
    /// fractional rates (143.86 Hz reads 143), so a rate 1 Hz above the reported one still fits.
    /// </summary>
    public static IReadOnlyList<int> FrameRatesFor(int refreshRate) =>
        CommonFrameRates.Where(rate => rate == 60 || rate <= refreshRate + 1).ToList();

    public static string Label(StreamResolution resolution) =>
        resolution == StreamResolution.Native ? "Native" : $"{(int)resolution}p";

    public VideoSize SizeFor(int screenWidth, int screenHeight) =>
        VideoSize.ForStream(screenWidth, screenHeight,
            Resolution == StreamResolution.Native ? int.MaxValue : (int)Resolution);

    public long BitRateFor(VideoSize size)
    {
        double pixels = (double)size.Width * size.Height / (1920 * 1080);
        long bitRate = (long)Math.Round(BaseBitRate * pixels * FrameRate / 60.0 * StreamQualities.Factor(Quality));
        return Math.Clamp(bitRate, MinBitRate, MaxBitRate);
    }
}
