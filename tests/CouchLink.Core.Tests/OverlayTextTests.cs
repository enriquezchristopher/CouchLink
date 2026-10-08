using CouchLink.Core.Audio;
using CouchLink.Core.Input;
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

    [Fact]
    public void The_audio_line_shows_buffer_repairs_and_the_output()
    {
        var s = new AudioClientStats(Packets: 1234, Repaired: 3, Concealed: 1, Late: 0, Discarded: 0, Underruns: 0,
            DecodeErrors: 0, DriftCorrections: 12, BufferMs: 15, Playing: true);

        Assert.Equal(
            "Audio buffer 15 ms  repaired 3  concealed 1  late 0\n" +
            "  1234 packets  drift 12  (Headphones, 2 ms)",
            OverlayText.Audio(s, "Headphones, 2 ms"));
        Assert.Equal("Audio: nothing from the host yet (muted: host is this PC)",
            OverlayText.Audio(default, "muted: host is this PC"));
    }

    [Fact]
    public void An_audio_line_goes_under_the_video_stats()
    {
        var sample = new StatsSample(60, 5, 0, 0, null, TimeSpan.Zero, null, TimeSpan.FromMilliseconds(3));

        Assert.EndsWith("Latency measuring...\nAudio x", OverlayText.Stats(sample, "software", "Audio x"));
        Assert.Equal("Collecting stats...\nAudio x", OverlayText.Stats(null, "software", "Audio x"));
        Assert.Equal("Collecting stats...", OverlayText.Stats(null, "software"));
    }

    [Fact]
    public void A_session_line_overrides_the_picture_status()
    {
        var expected = $"{OverlayText.Reconnecting}\n{OverlayText.LeaveHint}";
        Assert.Equal(expected, OverlayText.Status(true, false, TimeSpan.Zero, TimeSpan.FromSeconds(2), OverlayText.Reconnecting));
        Assert.Equal(expected, OverlayText.Status(false, true, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2), OverlayText.Reconnecting));
        Assert.Null(OverlayText.Status(true, false, TimeSpan.Zero, TimeSpan.FromSeconds(2), session: null));
    }

    [Fact]
    public void The_controls_panel_lists_every_control_with_its_current_keys()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(7);

        var text = OverlayText.Controls(settings);

        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Contains(KeyNames.Of(control), text);
        Assert.Contains($"{"Cross",-18}Space", text);
        Assert.Contains($"{"Square",-18}J / Left click", text);
        Assert.Contains("Mouse (sensitivity 7)", text);
        Assert.Contains("Ctrl+Alt+C", text);
    }
}
