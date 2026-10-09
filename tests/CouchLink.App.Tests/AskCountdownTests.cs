using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AskCountdownTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void At_the_start_the_bar_is_full()
    {
        var c = AskCountdown.For(TimeSpan.Zero, Timeout);
        Assert.Equal(1.0, c.Remaining, 3);
        Assert.Equal("Denied automatically in 30 s", c.Text);
    }

    [Fact]
    public void Halfway_the_bar_is_half()
    {
        var c = AskCountdown.For(TimeSpan.FromSeconds(15), Timeout);
        Assert.Equal(0.5, c.Remaining, 3);
        Assert.Equal("Denied automatically in 15 s", c.Text);
    }

    [Fact]
    public void Part_seconds_round_up()
    {
        Assert.Equal("Denied automatically in 22 s", AskCountdown.For(TimeSpan.FromSeconds(8.4), Timeout).Text);
    }

    [Fact]
    public void Past_the_timeout_it_stops_at_zero()
    {
        var c = AskCountdown.For(TimeSpan.FromSeconds(31), Timeout);
        Assert.Equal(0.0, c.Remaining, 3);
        Assert.Equal("Denied automatically in 0 s", c.Text);
    }

    [Fact]
    public void A_zero_timeout_does_not_divide_by_zero()
    {
        Assert.Equal(0.0, AskCountdown.For(TimeSpan.Zero, TimeSpan.Zero).Remaining, 3);
    }
}
