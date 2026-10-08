using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamSettingsTests
{
    [Theory]
    [InlineData(1920, 1080, 1080, 1920, 1080)]
    [InlineData(2560, 1080, 720, 1706, 720)]   // ultrawide keeps its shape
    [InlineData(3840, 2160, 1080, 1920, 1080)]
    [InlineData(1366, 768, 1080, 1366, 768)]   // never scaled up
    [InlineData(1366, 768, 540, 960, 540)]
    [InlineData(1365, 767, 1080, 1364, 766)]   // H.264 4:2:0 needs even sides
    public void Stream_size_keeps_the_aspect_and_stays_even(int w, int h, int max, int expectedW, int expectedH)
    {
        Assert.Equal(new VideoSize(expectedW, expectedH), VideoSize.ForStream(w, h, max));
    }

    [Fact]
    public void Native_uses_the_screen_size()
    {
        var settings = new StreamSettings(StreamResolution.Native, 60);
        Assert.Equal(new VideoSize(2560, 1080), settings.SizeFor(2560, 1080));
    }

    [Fact]
    public void Default_is_1080p_at_60()
    {
        Assert.Equal(new StreamSettings(StreamResolution.P1080, 60), StreamSettings.Default);
        Assert.Equal(StreamQuality.Balanced, StreamSettings.Default.Quality);
        Assert.Equal("1080p", StreamSettings.Label(StreamResolution.P1080));
        Assert.Equal("Native", StreamSettings.Label(StreamResolution.Native));
        Assert.Equal(
            [StreamResolution.Native, StreamResolution.P1080, StreamResolution.P900, StreamResolution.P720, StreamResolution.P540],
            StreamSettings.Resolutions);
    }

    [Theory]
    [InlineData(60, new[] { 60 })]
    [InlineData(75, new[] { 60, 75 })]
    [InlineData(144, new[] { 60, 75, 90, 120, 144 })]
    [InlineData(240, new[] { 60, 75, 90, 120, 144, 165, 240 })]
    [InlineData(30, new[] { 60 })]   // 60 is always offered
    [InlineData(143, new[] { 60, 75, 90, 120, 144 })] // Windows truncates 143.86 Hz
    [InlineData(164, new[] { 60, 75, 90, 120, 144, 165 })]
    [InlineData(239, new[] { 60, 75, 90, 120, 144, 165, 240 })]
    public void Only_rates_up_to_the_display_refresh_are_offered(int refresh, int[] expected)
    {
        Assert.Equal(expected, StreamSettings.FrameRatesFor(refresh));
    }

    [Theory]
    [InlineData(StreamResolution.P1080, 60, 1920, 1080, 10_000_000)]
    [InlineData(StreamResolution.P720, 60, 1920, 1080, 4_444_444)]
    [InlineData(StreamResolution.P1080, 144, 1920, 1080, 24_000_000)]
    [InlineData(StreamResolution.P1080, 60, 2560, 1080, 13_333_333)]
    [InlineData(StreamResolution.Native, 240, 2560, 1080, 53_333_333)]  // was capped at 30 Mbps
    [InlineData(StreamResolution.P540, 60, 1920, 1080, 2_500_000)]
    public void Bitrate_follows_size_and_frame_rate(StreamResolution resolution, int fps, int w, int h, long expected)
    {
        var settings = new StreamSettings(resolution, fps);
        Assert.Equal(expected, settings.BitRateFor(settings.SizeFor(w, h)));
    }

    [Theory]
    [InlineData(StreamQuality.Low, StreamResolution.P1080, 60, 1920, 1080, 6_000_000)]
    [InlineData(StreamQuality.Balanced, StreamResolution.P1080, 60, 1920, 1080, 10_000_000)]
    [InlineData(StreamQuality.High, StreamResolution.P1080, 60, 1920, 1080, 25_000_000)]
    [InlineData(StreamQuality.Max, StreamResolution.P1080, 60, 1920, 1080, 50_000_000)]
    [InlineData(StreamQuality.Balanced, StreamResolution.Native, 60, 2560, 1440, 17_777_778)]
    [InlineData(StreamQuality.Max, StreamResolution.Native, 60, 2560, 1440, 88_888_889)]
    [InlineData(StreamQuality.High, StreamResolution.P1080, 144, 1920, 1080, 60_000_000)]
    [InlineData(StreamQuality.Max, StreamResolution.P1080, 144, 1920, 1080, 100_000_000)] // capped
    [InlineData(StreamQuality.Low, StreamResolution.P540, 60, 1920, 1080, 2_000_000)]     // floor
    public void Quality_scales_the_bitrate(StreamQuality quality, StreamResolution resolution, int fps, int w, int h, long expected)
    {
        var settings = new StreamSettings(resolution, fps, quality);
        Assert.Equal(expected, settings.BitRateFor(settings.SizeFor(w, h)));
    }

    [Fact]
    public void Qualities_are_listed_low_to_max_with_labels()
    {
        Assert.Equal([StreamQuality.Low, StreamQuality.Balanced, StreamQuality.High, StreamQuality.Max], StreamQualities.All);
        Assert.Equal(["Low", "Balanced", "High", "Max"], StreamQualities.All.Select(StreamQualities.Label));
    }

    [Theory]
    [InlineData(StreamQuality.Low, false)]
    [InlineData(StreamQuality.Balanced, false)]
    [InlineData(StreamQuality.High, true)]
    [InlineData(StreamQuality.Max, true)]
    public void Only_high_and_max_use_the_slower_encoder(StreamQuality quality, bool slower)
    {
        Assert.Equal(slower, StreamQualities.UsesSlowerEncoder(quality));
    }

    [Fact]
    public void Undefined_qualities_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings(StreamResolution.P1080, 60, (StreamQuality)7));
    }

    [Fact]
    public void Bitrate_never_goes_below_the_minimum()
    {
        var settings = new StreamSettings(StreamResolution.P540, 60);
        Assert.Equal(StreamSettings.MinBitRate, settings.BitRateFor(new VideoSize(320, 240)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(100)]
    public void Uncommon_frame_rates_are_rejected(int fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings(StreamResolution.P1080, fps));
    }

    [Fact]
    public void Undefined_resolutions_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings((StreamResolution)1000, 60));
    }
}
