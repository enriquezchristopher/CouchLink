using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class TimingPacketTests
{
    [Fact]
    public void A_ping_round_trips()
    {
        var bytes = new byte[TimingPing.Size];
        new TimingPing(Slot: 7, ClientTicks: 123_456_789_012).WriteTo(bytes);

        Assert.True(TimingPing.TryParse(bytes, out var ping));
        Assert.Equal(new TimingPing(7, 123_456_789_012), ping);
    }

    [Fact]
    public void A_reply_round_trips_with_the_host_delay_in_microseconds()
    {
        var bytes = new byte[TimingReply.Size];
        new TimingReply(ClientTicks: -5, HostDelay: TimeSpan.FromMicroseconds(8_250)).WriteTo(bytes);

        Assert.True(TimingReply.TryParse(bytes, out var reply));
        Assert.Equal(-5, reply.ClientTicks);
        Assert.Equal(TimeSpan.FromMicroseconds(8_250), reply.HostDelay);
    }

    [Fact]
    public void A_negative_host_delay_is_sent_as_zero()
    {
        var bytes = new byte[TimingReply.Size];
        new TimingReply(1, TimeSpan.FromMilliseconds(-3)).WriteTo(bytes);
        Assert.True(TimingReply.TryParse(bytes, out var reply));
        Assert.Equal(TimeSpan.Zero, reply.HostDelay);
    }

    [Fact]
    public void Other_packets_are_not_timing_packets()
    {
        var ping = new byte[TimingPing.Size];
        new TimingPing(2, 9).WriteTo(ping);
        var keyframe = new byte[KeyframeRequest.Size];
        new KeyframeRequest(2).WriteTo(keyframe);

        Assert.False(TimingReply.TryParse(ping, out _));     // right size, wrong type
        Assert.False(TimingPing.TryParse(keyframe, out _));  // wrong size
        Assert.False(TimingPing.TryParse(ping.AsSpan(0, 15), out _));
        Assert.False(KeyframeRequest.TryParse(ping, out _)); // older code ignores it
    }
}
