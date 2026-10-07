using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class StreamDispatcherTests
{
    private static byte[] Typed(byte type)
    {
        var datagram = new byte[20];
        Wire.WriteHeader(datagram, type);
        return datagram;
    }

    [Fact]
    public void Datagrams_go_to_video_or_audio_by_type_and_the_rest_is_ignored()
    {
        var video = new List<byte[]>();
        var audio = new List<byte[]>();
        var datagrams = new[]
        {
            Typed(Wire.TypeVideoShard), Typed(Wire.TypeTimingReply), Typed(Wire.TypeAudio),
            Typed(Wire.TypeInput), Typed(99), new byte[3], new byte[20], Array.Empty<byte>(),
        };

        foreach (var d in datagrams)
            StreamDispatcher.Route(d, video.Add, audio.Add);

        Assert.Equal(new[] { Wire.TypeVideoShard, Wire.TypeTimingReply }, video.Select(d => d[3]));
        Assert.Equal(Wire.TypeAudio, Assert.Single(audio)[3]);
    }

    [Fact]
    public async Task Video_and_audio_on_one_port_both_arrive()
    {
        int frames = 0, audio = 0;
        var receiver = new VideoReceiver(port: 0);
        var to = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using var video = new VideoClient(() => { }, _ => Interlocked.Increment(ref frames), TimeProvider.System);
        using var dispatcher = new StreamDispatcher(receiver, video.Receive, _ => Interlocked.Increment(ref audio));
        using var udp = new UdpClient();

        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[5000], keyframe: true))
            udp.Send(p, p.Length, to);
        for (uint s = 1; s <= 20; s++)
        {
            var a = Packet(s).ToArray();
            udp.Send(a, a.Length, to);
        }

        await Until(() => Volatile.Read(ref frames) == 1 && Volatile.Read(ref audio) == 20);
    }

    [Fact]
    public async Task A_failing_audio_handler_does_not_stop_video()
    {
        int frames = 0, errors = 0;
        var receiver = new VideoReceiver(port: 0);
        var to = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using var video = new VideoClient(() => { }, _ => Interlocked.Increment(ref frames), TimeProvider.System);
        using var dispatcher = new StreamDispatcher(receiver, video.Receive,
            _ => throw new InvalidOperationException("audio broke"), _ => Interlocked.Increment(ref errors));
        using var udp = new UdpClient();

        var a = Packet(1).ToArray();
        udp.Send(a, a.Length, to);
        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[5000], keyframe: true))
            udp.Send(p, p.Length, to);

        await Until(() => Volatile.Read(ref frames) == 1 && Volatile.Read(ref errors) == 1);
    }
}
