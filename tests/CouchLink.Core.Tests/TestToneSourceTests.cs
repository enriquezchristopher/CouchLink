using System.Diagnostics;
using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class TestToneSourceTests
{
    [Fact]
    public void Beeps_for_100_ms_then_is_silent_until_the_next_second()
    {
        var frame = new short[AudioFormat.FrameValues];

        TestToneSource.Fill(frame, 0);
        Assert.InRange(frame.Max(), (short)7000, TestToneSource.Amplitude);
        TestToneSource.Fill(frame, 19);  // 95-100 ms: still beeping
        Assert.NotEqual(0, frame.Max());
        TestToneSource.Fill(frame, 20);  // 100 ms: quiet
        Assert.All(frame, v => Assert.Equal(0, v));
        TestToneSource.Fill(frame, 200); // 1 s: the next beep
        Assert.NotEqual(0, frame.Max());
    }

    [Fact]
    public void Both_channels_carry_the_same_tone()
    {
        var frame = new short[AudioFormat.FrameValues];
        TestToneSource.Fill(frame, 3);

        for (int i = 0; i < AudioFormat.FrameSamples; i++)
            Assert.Equal(frame[2 * i], frame[2 * i + 1]);
    }

    [Fact]
    public void Frames_come_in_real_time_and_only_the_first_is_a_discontinuity()
    {
        using var tone = new TestToneSource();
        var frame = new short[AudioFormat.FrameValues];
        var clock = Stopwatch.StartNew();

        Assert.True(tone.TryRead(frame, TimeSpan.FromSeconds(1), out bool first));
        Assert.True(first);
        for (int i = 1; i < 40; i++)
        {
            Assert.True(tone.TryRead(frame, TimeSpan.FromSeconds(1), out bool later));
            Assert.False(later);
        }

        Assert.InRange(clock.ElapsedMilliseconds, 180, 400); // frame 39 is due at 195 ms
        Assert.Equal("test tone", tone.Description);
    }
}
