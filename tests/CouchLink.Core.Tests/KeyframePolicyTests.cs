using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class KeyframePolicyTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Nothing_is_forced_without_a_request()
    {
        Assert.False(new KeyframePolicy().ShouldForce(Ms(0)));
    }

    [Fact]
    public void A_request_forces_keyframes_until_one_is_sent()
    {
        var policy = new KeyframePolicy();
        policy.Request();
        Assert.True(policy.ShouldForce(Ms(0)));
        Assert.True(policy.ShouldForce(Ms(10))); // the encoder had no frame yet; still pending
        policy.KeyframeSent(Ms(10));
        Assert.False(policy.ShouldForce(Ms(20)));
    }

    [Fact]
    public void Keyframes_are_at_least_MinInterval_apart()
    {
        var policy = new KeyframePolicy();
        policy.Request();
        policy.KeyframeSent(Ms(100));
        policy.Request();
        Assert.False(policy.ShouldForce(Ms(349)));
        Assert.True(policy.ShouldForce(Ms(350)));
    }

    [Fact]
    public void Many_requests_cost_one_keyframe()
    {
        var policy = new KeyframePolicy();
        for (int client = 0; client < 9; client++)
            policy.Request();
        Assert.True(policy.ShouldForce(Ms(0)));
        policy.KeyframeSent(Ms(0));
        Assert.False(policy.ShouldForce(Ms(1)));
    }

    [Fact]
    public void An_unrequested_keyframe_also_counts()
    {
        var policy = new KeyframePolicy();
        policy.KeyframeSent(Ms(0)); // e.g. the encoder's first frame
        policy.Request();
        Assert.False(policy.ShouldForce(Ms(100)));
        Assert.True(policy.ShouldForce(Ms(250)));
    }
}
