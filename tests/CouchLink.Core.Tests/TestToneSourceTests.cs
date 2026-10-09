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
}
