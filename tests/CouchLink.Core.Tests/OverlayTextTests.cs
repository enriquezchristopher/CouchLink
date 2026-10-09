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

        foreach (var control in Enum.GetValues<PadControl>().Except(KeyNames.RightStick))
            Assert.Contains(KeyNames.Of(control), text);
        Assert.Contains($"{"Cross",-18}Space", text);
        Assert.Contains($"{"Square",-18}J / Left click", text);
        Assert.Contains("Mouse (sensitivity 7)", text);
        Assert.Contains("Ctrl+Alt+C", text);
    }

    [Fact]
    public void The_built_in_layout_keeps_the_plain_title()
    {
        var text = OverlayText.Controls(new ControlSettings());
        Assert.StartsWith("Controls (F1 hides, Ctrl+Alt+C changes keys)\n", text);
    }

    [Fact]
    public void A_loaded_profile_is_named_in_the_title_and_shows_changes()
    {
        var settings = new ControlSettings();
        settings.Apply(new ControlProfile("2K22 Café", null, 5, false,
            KeyLayout.CreateDefault().Current, new Dictionary<PadControl, string>()));
        Assert.StartsWith("Controls: 2K22 Café (F1 hides, Ctrl+Alt+C changes keys)\n", OverlayText.Controls(settings));

        settings.SetSensitivityStep(9);
        Assert.StartsWith("Controls: 2K22 Café (changed) (F1 hides", OverlayText.Controls(settings));
    }

    [Fact]
    public void Labels_lead_their_rows_and_the_column_widens_to_fit()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Shoot from range");

        var text = OverlayText.Controls(settings);

        int width = "Shoot from range (Square)".Length + 2;
        Assert.Contains("Shoot from range (Square)".PadRight(width) + "J / Left click", text);
        Assert.Contains("Cross".PadRight(width) + "K", text);
        Assert.Contains("Right stick".PadRight(width) + "Mouse", text);
    }

    [Fact]
    public void The_default_panel_hides_the_right_stick_keys_and_keeps_its_width()
    {
        var text = OverlayText.Controls(new ControlSettings());

        Assert.DoesNotContain("Right stick up", text);
        Assert.Contains($"{"Cross",-18}K\n", text);
        Assert.EndsWith($"{"Right stick",-18}Mouse (sensitivity {ControlSettings.DefaultStep})", text);
    }

    [Fact]
    public void One_right_stick_key_lists_all_four_directions()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.RightUp, 0x68);
        settings.SetLabel(PadControl.RightUp, "Pro stick up");

        var text = OverlayText.Controls(settings);

        int width = "Pro stick up (Right stick up)".Length + 2;
        Assert.Contains("Pro stick up (Right stick up)".PadRight(width) + "Num 8", text);
        Assert.Contains("Right stick down".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick left".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick right".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick".PadRight(width) + "Mouse", text); // the mouse still works when no key is held
    }

    [Fact]
    public void A_right_stick_label_without_a_key_stays_hidden()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.RightUp, "Pro stick up");

        var text = OverlayText.Controls(settings);

        Assert.DoesNotContain("Pro stick up", text);
        Assert.Contains($"{"Cross",-18}K\n", text);
    }
}
