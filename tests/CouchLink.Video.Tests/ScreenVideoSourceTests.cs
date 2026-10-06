using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

public class ScreenVideoSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Interval60 = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCapture _capture;
    private readonly List<FakeEncoder> _opened = [];
    private readonly DateTimeOffset _start;

    public ScreenVideoSourceTests()
    {
        _capture = new FakeCapture(_time);
        _start = _time.GetUtcNow();
    }

    private TimeSpan Elapsed => _time.GetUtcNow() - _start;

    private ScreenVideoSource Source(StreamSettings? settings = null, params string[] failing) =>
        new(_capture, [EncoderChoice.Amf, EncoderChoice.Software],
            (name, size) =>
            {
                if (failing.Contains(name))
                    throw new InvalidOperationException($"{name} is not available");
                var encoder = new FakeEncoder(name, size);
                _opened.Add(encoder);
                return encoder;
            },
            settings ?? StreamSettings.Default, _time, t => _time.Advance(t));

    private EncodedFrame Next(ScreenVideoSource source, bool force = false)
    {
        Assert.True(source.TryGetFrame(force, Timeout, out var frame));
        return frame;
    }

    [Fact]
    public void A_changed_screen_is_sent_at_once()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        var frame = Next(source);

        Assert.True(frame.Keyframe);
        Assert.False(frame.Paused);
        Assert.Equal(TimeSpan.Zero, Elapsed);
    }

    [Fact]
    public void Changes_faster_than_the_frame_rate_wait_for_the_next_slot()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        Next(source);
        Next(source);

        Assert.Equal(Interval60, Elapsed);
    }

    [Fact]
    public void A_still_screen_repeats_the_last_image_each_frame()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        Next(source);
        var repeat = Next(source); // script empty: no change

        Assert.Equal(Interval60, Elapsed);
        Assert.False(repeat.Keyframe);
        Assert.Equal(2, _opened[0].ForcedKeyframes.Count);
    }

    [Fact]
    public void Encoding_time_does_not_slow_the_frame_rate()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        _opened[0].OnEncode = () => _time.Advance(TimeSpan.FromMilliseconds(5));

        Next(source);
        Next(source);
        Next(source);

        // Frames start every interval; only the last one's 5 ms of encoding shows on top.
        Assert.Equal(2 * Interval60 + TimeSpan.FromMilliseconds(5), Elapsed);
    }

    [Fact]
    public void The_chosen_frame_rate_sets_the_frame_interval()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source(new StreamSettings(StreamResolution.P1080, 120));

        Next(source);
        Next(source);

        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120), Elapsed);
    }

    [Fact]
    public void Nothing_is_returned_when_no_frame_is_due_within_the_timeout()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source);

        Assert.False(source.TryGetFrame(false, TimeSpan.FromMilliseconds(5), out _));
        Assert.Equal(TimeSpan.FromMilliseconds(5), Elapsed);
    }

    [Fact]
    public void Lost_capture_sends_paused_frames_and_resumes_with_a_keyframe()
    {
        foreach (var status in new[] { CaptureStatus.NewFrame, CaptureStatus.Lost, CaptureStatus.Lost, CaptureStatus.NewFrame })
            _capture.Script.Enqueue(status);
        using var source = Source();

        var frames = Enumerable.Range(0, 4).Select(_ => Next(source)).ToList();

        Assert.Equal([false, true, true, false], frames.Select(f => f.Paused));
        Assert.True(frames[3].Keyframe, "the first frame after a loss is a keyframe");
        Assert.Equal(3 * Interval60, Elapsed); // paused frames still keep the frame rate
        Assert.False(source.Paused);
    }

    [Fact]
    public void A_new_screen_size_reopens_the_encoder_with_a_keyframe()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source(new StreamSettings(StreamResolution.P720, 60));
        Next(source);

        _capture.Width = 2560;
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        var frame = Next(source);

        Assert.Equal(2, _opened.Count);
        Assert.True(_opened[0].Disposed);
        Assert.Equal(new VideoSize(1706, 720), _opened[1].Size);
        Assert.Equal(new VideoSize(1706, 720), source.Size);
        Assert.True(frame.Keyframe);
    }

    [Fact]
    public void The_stream_size_comes_from_the_settings()
    {
        _capture.Width = 2560;
        using var source = Source(new StreamSettings(StreamResolution.P720, 60));
        Assert.Equal(new VideoSize(1706, 720), source.Size);
    }

    [Fact]
    public void Falls_back_to_the_software_encoder()
    {
        using var source = Source(null, EncoderChoice.Amf);

        Assert.Equal(EncoderChoice.Software, source.EncoderName);
        Assert.False(source.IsHardware);
        Assert.Equal(["h264_amf: h264_amf is not available"], source.SkippedEncoders);
    }

    [Fact]
    public void No_encoder_at_all_throws_with_every_reason()
    {
        var e = Assert.Throws<InvalidOperationException>(() => Source(null, EncoderChoice.Amf, EncoderChoice.Software));
        Assert.Contains("h264_amf is not available", e.Message);
        Assert.Contains("libx264 is not available", e.Message);
    }

    [Fact]
    public void A_requested_keyframe_is_forced()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source, force: true);
        Assert.True(_opened[0].ForcedKeyframes[0]);
    }

    [Fact]
    public void An_encoder_without_a_packet_yet_returns_nothing()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        _opened[0].ProducePackets = false;
        Assert.False(source.TryGetFrame(false, Timeout, out _));
    }

    [Fact]
    public void Dispose_releases_the_encoder_and_the_capture()
    {
        var source = Source();
        source.Dispose();
        Assert.True(_opened[0].Disposed);
        Assert.True(_capture.Disposed);
    }
}
