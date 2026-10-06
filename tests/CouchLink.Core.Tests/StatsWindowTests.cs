using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StatsWindowTests
{
    private static VideoClientStats S(long packets, long expected, long missing, long recovered,
        TimeSpan? rtt = null, TimeSpan hostDelay = default) =>
        new(new VideoReceiveStats(packets, 0, 0, expected, missing, recovered), 0, 0, 0, false, false, rtt, hostDelay);

    [Fact]
    public void The_first_update_only_records()
    {
        Assert.Null(new StatsWindow().Update(S(0, 0, 0, 0), 0, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Rates_are_per_second_over_the_last_window()
    {
        var w = new StatsWindow();
        w.Update(S(1000, 900, 0, 5), framesShown: 100, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Null(w.Update(S(1100, 1000, 0, 5), 110, TimeSpan.Zero, TimeSpan.FromMilliseconds(500))); // too soon

        var s = w.Update(S(2000, 1900, 19, 12, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(9)),
            framesShown: 160, clientDelay: TimeSpan.FromMilliseconds(5), now: TimeSpan.FromSeconds(1));

        Assert.NotNull(s);
        Assert.Equal(60, s.Value.Fps, 3);
        Assert.Equal(1000.0 * VideoShardPacket.Size * 8 / 1e6, s.Value.Mbps, 3); // 1000 packets in 1 s
        Assert.Equal(1.9, s.Value.LossPercent, 3);                              // 19 of 1000 data shards
        Assert.Equal(7, s.Value.FecRepairs);
        Assert.Equal(TimeSpan.FromMilliseconds(1), s.Value.Network);            // half the round trip
        Assert.Equal(TimeSpan.FromMilliseconds(9 + 1 + 5), s.Value.Latency);
    }

    [Fact]
    public void Latency_is_unknown_until_a_round_trip_is_measured()
    {
        var w = new StatsWindow();
        w.Update(S(0, 0, 0, 0), 0, TimeSpan.Zero, TimeSpan.Zero);
        var s = w.Update(S(10, 10, 0, 0), 1, TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1));
        Assert.Null(s!.Value.Latency);
        Assert.Null(s.Value.Network);
    }
}
