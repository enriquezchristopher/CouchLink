using System.Diagnostics;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class TestPatternTests
{
    [Fact]
    public void Created_frames_verify()
    {
        Assert.True(TestPattern.Verify(TestPattern.Create(7, keyframe: false), keyframe: false));
        var keyframe = TestPattern.Create(8, keyframe: true);
        Assert.Equal(TestPattern.KeyframeBytes, keyframe.Length);
        Assert.True(TestPattern.Verify(keyframe, keyframe: true));
    }

    [Fact]
    public void Any_changed_byte_fails()
    {
        foreach (int index in new[] { 0, 8, 13, TestPattern.DeltaFrameBytes - 1 })
        {
            var frame = TestPattern.Create(3, keyframe: false);
            frame[index] ^= 0x01;
            Assert.False(TestPattern.Verify(frame, keyframe: false), $"byte {index} changed");
        }
    }

    [Fact]
    public void Wrong_keyframe_flag_or_truncation_fails()
    {
        var frame = TestPattern.Create(5, keyframe: false);
        Assert.False(TestPattern.Verify(frame, keyframe: true));
        Assert.False(TestPattern.Verify(frame.AsSpan(0, frame.Length - 1), keyframe: false));
        Assert.False(TestPattern.Verify(frame.AsSpan(0, 4), keyframe: false));
    }

    [Fact]
    public void Source_starts_with_a_keyframe_and_forces_one_on_request()
    {
        using var source = new TestPatternSource(TimeSpan.Zero);
        var timeout = TimeSpan.FromSeconds(1);

        Assert.True(source.TryGetFrame(false, timeout, out var first));
        Assert.True(source.TryGetFrame(false, timeout, out var second));
        Assert.True(source.TryGetFrame(true, timeout, out var forced));

        Assert.True(first.Keyframe);
        Assert.False(second.Keyframe);
        Assert.True(forced.Keyframe);
        Assert.True(TestPattern.Verify(second.Data.Span, keyframe: false));
        Assert.True(TestPattern.Verify(forced.Data.Span, keyframe: true));
    }

    [Fact]
    public void Source_paces_frames()
    {
        using var source = new TestPatternSource(TimeSpan.FromMilliseconds(20));
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 6; i++)
            Assert.True(source.TryGetFrame(false, TimeSpan.FromSeconds(1), out _));
        Assert.True(clock.ElapsedMilliseconds >= 90, $"6 frames took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Source_returns_false_when_no_frame_is_due_within_the_timeout()
    {
        using var source = new TestPatternSource(TimeSpan.FromSeconds(1));
        Assert.True(source.TryGetFrame(false, TimeSpan.FromMilliseconds(10), out _)); // first frame is due now
        Assert.False(source.TryGetFrame(false, TimeSpan.FromMilliseconds(10), out _));
    }
}
