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
    [InlineData(StreamResolution.Native, 240, 2560, 1080, 30_000_000)]  // capped
    [InlineData(StreamResolution.P540, 60, 1920, 1080, 2_500_000)]
    public void Bitrate_follows_size_and_frame_rate(StreamResolution resolution, int fps, int w, int h, long expected)
    {
        var settings = new StreamSettings(resolution, fps);
        Assert.Equal(expected, settings.BitRateFor(settings.SizeFor(w, h)));
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
