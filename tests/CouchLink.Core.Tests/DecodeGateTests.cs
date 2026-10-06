using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class DecodeGateTests
{
    private static AssembledFrame F(uint number, bool keyframe = false) => new(number, keyframe, []);
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Starts_waiting_and_skips_delta_frames_until_a_keyframe()
    {
        var gate = new DecodeGate();
        Assert.True(gate.WaitingForKeyframe);
        Assert.False(gate.Accept(F(5)));
        Assert.True(gate.Accept(F(6, keyframe: true)));
        Assert.True(gate.Accept(F(7)));
        Assert.False(gate.WaitingForKeyframe);
        Assert.Equal(1, gate.FramesSkipped);
    }

    [Fact]
    public void A_gap_in_frame_numbers_waits_for_the_next_keyframe()
    {
        var gate = new DecodeGate();
        Assert.True(gate.Accept(F(1, keyframe: true)));
        Assert.True(gate.Accept(F(2)));
        Assert.False(gate.Accept(F(4))); // frame 3 never arrived at all
        Assert.False(gate.Accept(F(5)));
        Assert.True(gate.Accept(F(6, keyframe: true)));
        Assert.True(gate.Accept(F(7)));
    }

    [Fact]
    public void A_lost_frame_waits_for_the_next_keyframe()
    {
        var gate = new DecodeGate();
        gate.Accept(F(1, keyframe: true));
        gate.Accept(F(2));
        gate.FrameLost(3);
        Assert.True(gate.WaitingForKeyframe);
        Assert.False(gate.Accept(F(4)));
        Assert.True(gate.Accept(F(5, keyframe: true)));
    }

    [Fact]
    public void A_lost_frame_older_than_the_last_delivered_is_ignored()
    {
        var gate = new DecodeGate();
        gate.Accept(F(1, keyframe: true));
        gate.Accept(F(2));
        gate.Accept(F(3));
        gate.FrameLost(2);
        Assert.True(gate.Accept(F(4)));
    }

    [Fact]
    public void Keyframe_requests_repeat_every_interval_while_waiting()
    {
        var gate = new DecodeGate();
        Assert.True(gate.ShouldRequestKeyframe(Ms(0)));
        Assert.False(gate.ShouldRequestKeyframe(Ms(299)));
        Assert.True(gate.ShouldRequestKeyframe(Ms(300)));
        gate.Accept(F(1, keyframe: true));
        Assert.False(gate.ShouldRequestKeyframe(Ms(1000)));
        Assert.Equal(2, gate.KeyframeRequests);

        gate.FrameLost(2);
        Assert.True(gate.ShouldRequestKeyframe(Ms(1001))); // a new loss asks right away
    }

    [Fact]
    public void Frame_numbers_wrap_around()
    {
        var gate = new DecodeGate();
        Assert.True(gate.Accept(F(uint.MaxValue, keyframe: true)));
        Assert.True(gate.Accept(F(0)));
    }
}
