using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class OverlayTextTests
{
    [Fact]
    public void Stats_show_every_number_the_overlay_promises()
    {
        var sample = new StatsSample(Fps: 59.6, Mbps: 12.34, LossPercent: 0.26, FecRepairs: 3,
            Latency: TimeSpan.FromMilliseconds(23.4), HostDelay: TimeSpan.FromMilliseconds(14.1),
            Network: TimeSpan.FromMilliseconds(0.6), ClientDelay: TimeSpan.FromMilliseconds(8.7));

        Assert.Equal(
            "60 fps  12.3 Mbps  (D3D11VA)\n" +
            "Packet loss 0.3%  FEC repairs 3/s\n" +
            "Latency ~23 ms (host 14 + network 1 + client 9)",
            OverlayText.Stats(sample, "D3D11VA"));
    }

    [Fact]
    public void Unknown_latency_and_no_sample_yet_say_so()
    {
        var sample = new StatsSample(60, 5, 0, 0, null, TimeSpan.Zero, null, TimeSpan.FromMilliseconds(3));
        Assert.EndsWith("Latency measuring...", OverlayText.Stats(sample, "software"));
        Assert.Equal("Collecting stats...", OverlayText.Stats(null, "software"));
    }

    [Theory]
    [InlineData(false, false, 0, "Waiting for the host's picture...\nCtrl+Alt+Q to leave")]
    [InlineData(false, true, 5, "Waiting for the host's picture...\nCtrl+Alt+Q to leave")]
    [InlineData(true, true, 0, OverlayText.Paused)]
    [InlineData(true, false, 0, null)]
    [InlineData(true, false, 2, "No picture from the host\nCtrl+Alt+Q to leave")]
    [InlineData(true, true, 2, "No picture from the host\nCtrl+Alt+Q to leave")]
    public void Status_says_why_there_is_no_live_picture(bool shown, bool paused, int secondsQuiet, string? expected)
    {
        Assert.Equal(expected, OverlayText.Status(shown, paused, TimeSpan.FromSeconds(secondsQuiet), TimeSpan.FromSeconds(2)));
    }
}
