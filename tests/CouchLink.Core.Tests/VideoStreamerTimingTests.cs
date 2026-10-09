using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class VideoStreamerTimingTests
{
    private sealed class RecordingSender : IVideoPacketSender
    {
        public List<(byte[] Packet, IPEndPoint Target)> Sent { get; } = [];

        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
        {
            lock (Sent)
                foreach (var p in packets)
                    foreach (var t in targets)
                        Sent.Add((p, t));
        }

        public void Dispose() { }
    }

    /// <summary>Hands out a frame every 5 ms whose image is always 8 ms old.</summary>
    private sealed class AgedSource : IEncodedVideoSource
    {
        public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
        {
            Thread.Sleep(5);
            frame = new EncodedFrame(new byte[100], forceKeyframe, CaptureToEncoded: TimeSpan.FromMilliseconds(8));
            return true;
        }

        public void Dispose() { }
    }

    [Fact]
    public void A_timing_ping_is_answered_on_the_video_port()
    {
        var sender = new RecordingSender();
        using var streamer = new VideoStreamer(new AgedSource(), sender, 47802, TimeProvider.System);
        var from = IPAddress.Parse("192.168.1.23");

        streamer.ReplyToTimingPing(new TimingPing(3, ClientTicks: 4242), from);

        (byte[] Packet, IPEndPoint Target) reply;
        lock (sender.Sent)
            reply = sender.Sent.Single(s => TimingReply.TryParse(s.Packet, out _));
        Assert.Equal(new IPEndPoint(from, 47802), reply.Target);
        Assert.True(TimingReply.TryParse(reply.Packet, out var parsed));
        Assert.Equal(4242, parsed.ClientTicks);
    }
}
