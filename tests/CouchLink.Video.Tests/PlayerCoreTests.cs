using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

public class PlayerCoreTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakePresenter _presenter = new();
    private readonly List<FakeDecoder> _decoders = [];
    private VideoClientStats _stats = default;
    private int _decodeFailed;
    private Func<uint, bool> _hardwareFailsOn = _ => false;
    private string? _audio;
    private string? _session;
    private string? _controls;

    private PlayerCore Core() => new(
        hardware =>
        {
            var d = new FakeDecoder(hardware) { FailOn = hardware ? _hardwareFailsOn : _ => false };
            _decoders.Add(d);
            return d;
        },
        _presenter, () => _stats, () => _decodeFailed++, _time, audioLine: () => _audio, sessionStatus: () => _session, controlsText: () => _controls);

    private static AssembledFrame F(uint number, bool keyframe = false, TimeSpan assembly = default) =>
        new(number, keyframe, BitConverter.GetBytes(number), AssemblyTime: assembly);

    private static uint? ShownFrame((DecodedPicture? Picture, string? Status, string? Stats, string? Controls, string? Hint) s) =>
        s.Picture is { } p ? (uint)p.Frame : null;

    [Fact]
    public void Decodes_every_frame_but_presents_only_the_newest()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Enqueue(F(2));
        core.Enqueue(F(3));

        core.Run();

        Assert.Equal([1u, 2u, 3u], _decoders[0].Decoded);
        Assert.Equal(3u, ShownFrame(Assert.Single(_presenter.Shown)));
        Assert.Equal(1, core.FramesShown);
    }

    [Fact]
    public void A_hardware_decode_error_switches_to_software_and_resyncs()
    {
        _hardwareFailsOn = n => n == 2;
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        core.Enqueue(F(2));
        core.Enqueue(F(3));                 // after the failure: dropped, it depends on frame 2

        core.Run();

        Assert.Equal(2, _decoders.Count);
        Assert.True(_decoders[0].Disposed);
        Assert.False(_decoders[1].IsHardware);
        Assert.Equal("software", core.DecoderName);
        Assert.Equal(1, _decodeFailed);
        Assert.Empty(_decoders[1].Decoded);

        core.Enqueue(F(4, keyframe: true));
        core.Run();
        Assert.Equal([4u], _decoders[1].Decoded);
        Assert.Equal(4u, ShownFrame(_presenter.Shown[^1]));
    }

    [Fact]
    public void A_software_decode_error_only_resyncs()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();                          // hardware works...
        _hardwareFailsOn = _ => false;
        _decoders[0].FailOn = n => n == 2;   // ...then a frame is corrupt
        core.Enqueue(F(2));
        core.Run();                          // switches to software

        _decoders[1].FailOn = n => n == 5;
        core.Enqueue(F(5));
        core.Run();

        Assert.Equal(2, _decoders.Count);    // no third decoder
        Assert.Equal(2, _decodeFailed);
    }

    [Fact]
    public void A_backlog_is_dropped_and_resyncs_on_a_keyframe()
    {
        using var core = Core();
        for (uint n = 1; n <= PlayerCore.MaxBacklog + 4; n++)
            core.Enqueue(F(n));              // the player fell far behind; no keyframe in the backlog

        core.Run();

        Assert.Empty(_decoders[0].Decoded);
        Assert.Equal(1, _decodeFailed);
    }

    [Fact]
    public void A_backlog_with_a_keyframe_starts_from_it()
    {
        using var core = Core();
        for (uint n = 1; n <= PlayerCore.MaxBacklog + 4; n++)
            core.Enqueue(F(n, keyframe: n == 8));

        core.Run();

        Assert.Equal([8u, 9u, 10u], _decoders[0].Decoded);
        Assert.Equal(0, _decodeFailed);
    }

    [Fact]
    public void Shows_waiting_text_before_the_first_frame()
    {
        using var core = Core();
        core.Run();
        var shown = Assert.Single(_presenter.Shown);
        Assert.Null(shown.Picture);
        Assert.Equal($"{OverlayText.Waiting}\n{OverlayText.LeaveHint}", shown.Status);
    }

    [Fact]
    public void Shows_paused_text_while_the_host_is_paused()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        _stats = _stats with { HostPaused = true };

        core.Run();                          // status changed: redraw at once

        Assert.Equal(OverlayText.Paused, _presenter.Shown[^1].Status);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1])); // over the last picture
    }

    [Fact]
    public void Redraws_while_no_frames_arrive()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        core.Run();
        Assert.Single(_presenter.Shown);     // nothing changed: no redraw yet

        _time.Advance(PlayerCore.RedrawInterval);
        core.Run();

        Assert.Equal(2, _presenter.Shown.Count);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1]));
    }

    [Fact]
    public void Stats_appear_when_switched_on()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();

        core.ShowStats = true;
        core.Run();
        Assert.Equal("Collecting stats...", _presenter.Shown[^1].Stats);

        _time.Advance(StatsWindow.Length);
        core.Run();
        Assert.Contains("fps", _presenter.Shown[^1].Stats);

        core.ShowStats = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Stats);
    }

    [Fact]
    public void Stats_include_the_audio_line()
    {
        _audio = "Audio buffer 15 ms";
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.ShowStats = true;

        core.Run();

        Assert.Equal("Collecting stats...\nAudio buffer 15 ms", _presenter.Shown[^1].Stats);
    }

    [Fact]
    public void Client_delay_is_assembly_plus_receive_to_present()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true, assembly: TimeSpan.FromMilliseconds(2)));
        _time.Advance(TimeSpan.FromMilliseconds(3)); // waited 3 ms in the queue

        core.Run();

        Assert.Equal(TimeSpan.FromMilliseconds(5), core.ClientDelay);
    }

    [Fact]
    public void FFmpeg_falling_back_to_software_by_itself_switches_to_the_software_decoder()
    {
        using var core = Core();
        _decoders[0].LosesHardwareOn = n => n == 2; // e.g. a profile the GPU can't decode
        core.Enqueue(F(1, keyframe: true));
        core.Enqueue(F(2));

        core.Run();

        Assert.Equal(2, _decoders.Count);
        Assert.True(_decoders[0].Disposed);
        Assert.False(_decoders[1].IsHardware);
        Assert.Equal(1, _decodeFailed); // the new decoder starts at a keyframe
    }

    [Fact]
    public void Says_so_when_the_host_goes_quiet()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();

        _time.Advance(PlayerCore.QuietAfter);
        core.Run();

        Assert.Equal($"{OverlayText.NoPicture}\n{OverlayText.LeaveHint}", _presenter.Shown[^1].Status);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1])); // over the last picture
    }

    [Fact]
    public void Session_status_overrides_the_picture_status()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Status);

        _session = OverlayText.Reconnecting;
        core.Run();                          // status changed: redraw at once

        Assert.Equal($"{OverlayText.Reconnecting}\n{OverlayText.LeaveHint}", _presenter.Shown[^1].Status);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1])); // over the last picture
    }

    [Fact]
    public void The_start_hint_shows_for_5s_from_the_first_frame()
    {
        using var core = Core();
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint); // nothing shown yet: no hint

        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Equal(OverlayText.StartHint, _presenter.Shown[^1].Hint);

        _time.Advance(PlayerCore.StartHintFor - TimeSpan.FromMilliseconds(1));
        core.Run();
        Assert.Equal(OverlayText.StartHint, _presenter.Shown[^1].Hint);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);
    }

    [Fact]
    public void F1_hides_the_start_hint_for_good()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();

        core.ShowControls = true;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);

        core.ShowControls = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);
    }

    [Fact]
    public void The_controls_panel_shows_the_current_text_while_on()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Controls);

        _controls = "Cross  K";
        core.ShowControls = true;
        core.Run();
        Assert.Equal("Cross  K", _presenter.Shown[^1].Controls);

        _controls = "Cross  Space"; // an edit in the controls editor
        core.Run();                 // text changed: redraw at once
        Assert.Equal("Cross  Space", _presenter.Shown[^1].Controls);

        core.ShowControls = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Controls);
    }
}
