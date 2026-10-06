using System.Net;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamTargetsTests
{
    private const int VideoPort = 47802;
    private static readonly IPAddress A = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress B = IPAddress.Parse("192.168.1.21");
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void New_client_needs_a_keyframe_and_known_client_does_not()
    {
        var targets = new StreamTargets(VideoPort);
        Assert.True(targets.Seen(2, A, Ms(0)));
        Assert.False(targets.Seen(2, A, Ms(8)));
    }

    [Fact]
    public void Client_gets_video_on_its_address_and_the_video_port()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.Equal(new IPEndPoint(A, VideoPort), Assert.Single(targets.Current(Ms(0))));
    }

    [Fact]
    public void Client_that_moves_to_another_address_needs_a_keyframe()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.True(targets.Seen(2, B, Ms(8)));
        Assert.Equal(new IPEndPoint(B, VideoPort), Assert.Single(targets.Current(Ms(8))));
    }

    [Fact]
    public void Silent_client_is_dropped_after_the_timeout_and_is_new_when_it_returns()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.Single(targets.Current(StreamTargets.Timeout - Ms(1)));
        Assert.Empty(targets.Current(StreamTargets.Timeout));
        Assert.True(targets.Seen(2, A, StreamTargets.Timeout + Ms(1)));
    }

    [Fact]
    public void Silent_client_counts_as_new_even_before_Current_runs()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.True(targets.Seen(2, A, StreamTargets.Timeout));
    }

    [Fact]
    public void Two_slots_on_one_PC_get_one_copy()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        targets.Seen(3, A, Ms(0));
        Assert.Single(targets.Current(Ms(0)));
    }
}
