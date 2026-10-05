using CouchLink.Core.Input;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class SendPolicyTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross };

    [Fact]
    public void First_state_is_always_sent()
    {
        Assert.True(new SendPolicy().ShouldSend(PadState.Neutral, Ms(0)));
    }

    [Fact]
    public void Unchanged_state_waits_for_8ms()
    {
        var p = new SendPolicy();
        p.ShouldSend(PadState.Neutral, Ms(0));
        Assert.False(p.ShouldSend(PadState.Neutral, Ms(7.9)));
        Assert.True(p.ShouldSend(PadState.Neutral, Ms(8)));
        Assert.False(p.ShouldSend(PadState.Neutral, Ms(9)));
    }

    [Fact]
    public void Changed_state_is_sent_immediately()
    {
        var p = new SendPolicy();
        p.ShouldSend(PadState.Neutral, Ms(0));
        Assert.True(p.ShouldSend(Pressed, Ms(1)));
        Assert.True(p.ShouldSend(PadState.Neutral, Ms(2)));
    }
}
