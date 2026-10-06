using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class VideoClientTimingTests
{
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Pings_go_out_once_a_second_with_the_clients_clock()
    {
        var time = new FakeTimeProvider();
        var pings = new List<long>();
        using var client = new VideoClient(new VideoReceiver(port: 0), () => { }, _ => { }, time,
            sendTimingPing: t => { lock (pings) pings.Add(t); });

        for (int i = 0; i < 25; i++)
            time.Advance(VideoClient.TickInterval); // 1.25 s

        lock (pings)
        {
            Assert.Equal(2, pings.Count); // at the first tick, then 1 s later
            Assert.Equal(VideoClient.PingInterval.Ticks, pings[1] - pings[0]);
        }
    }

    [Fact]
    public async Task A_reply_sets_the_round_trip_and_the_host_delay()
    {
        var time = new FakeTimeProvider();
        var receiver = new VideoReceiver(port: 0);
        int port = receiver.LocalPort;
        long? sentAt = null;
        using var client = new VideoClient(receiver, () => { }, _ => { }, time,
            sendTimingPing: t => sentAt ??= t);
        time.Advance(VideoClient.TickInterval); // first ping
        Assert.NotNull(sentAt);

        time.Advance(TimeSpan.FromMilliseconds(2)); // the reply comes back 2 ms later
        var reply = new byte[TimingReply.Size];
        new TimingReply(sentAt.Value, TimeSpan.FromMilliseconds(9)).WriteTo(reply);
        using var udp = new UdpClient();
        udp.Send(reply, reply.Length, new IPEndPoint(IPAddress.Loopback, port));

        await Until(() => client.Stats.RoundTrip is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(2), client.Stats.RoundTrip);
        Assert.Equal(TimeSpan.FromMilliseconds(9), client.Stats.HostDelay);
    }

    [Fact]
    public async Task DecodeFailed_asks_the_host_for_a_keyframe_at_once()
    {
        var time = new FakeTimeProvider();
        int requests = 0, frames = 0;
        var receiver = new VideoReceiver(port: 0);
        int port = receiver.LocalPort;
        using var client = new VideoClient(receiver, () => Interlocked.Increment(ref requests),
            _ => Interlocked.Increment(ref frames), time);
        using var udp = new UdpClient();
        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[100], keyframe: true))
            udp.Send(p, p.Length, new IPEndPoint(IPAddress.Loopback, port));
        await Until(() => Volatile.Read(ref frames) == 1); // decoding normally now
        int before = Volatile.Read(ref requests);

        client.DecodeFailed();

        Assert.Equal(before + 1, Volatile.Read(ref requests)); // sent by DecodeFailed itself, not a later tick
        Assert.True(client.Stats.WaitingForKeyframe);
    }
}
