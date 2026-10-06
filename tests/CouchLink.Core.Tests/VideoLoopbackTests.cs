using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

/// <summary>Spec section 9 loopback test: host streamer and client pipeline on one PC over real UDP.</summary>
public class VideoLoopbackTests
{
    private sealed class LossySender(IVideoPacketSender inner, Func<VideoShardHeader, bool> drop) : IVideoPacketSender
    {
        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets) =>
            inner.Send(packets.Where(p => !(VideoShardPacket.TryParse(p, out var h) && drop(h))).ToList(), targets);

        public void Dispose() => inner.Dispose();
    }

    /// <summary>A streamer sending a 100 fps test pattern to one client on localhost.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly Timer _keepAlive;
        private int _delivered, _corrupt, _firstWasKeyframe = -1;

        public Rig(Func<IVideoPacketSender, IVideoPacketSender>? wrap = null)
        {
            var receiver = new VideoReceiver(port: 0);
            IVideoPacketSender sender = new VideoSender();
            if (wrap is not null)
                sender = wrap(sender);
            Streamer = new VideoStreamer(
                new TestPatternSource(TimeSpan.FromMilliseconds(10)), sender, receiver.LocalPort, TimeProvider.System);
            Client = new VideoClient(receiver, Streamer.RequestKeyframe, OnFrame, TimeProvider.System);
            // A real client's input packets keep it in the host's targets; do the same here.
            _keepAlive = new Timer(_ => Streamer.ClientSeen(2, IPAddress.Loopback), null, 0, 100);
        }

        public VideoStreamer Streamer { get; }
        public VideoClient Client { get; }
        public int Delivered => Volatile.Read(ref _delivered);
        public int Corrupt => Volatile.Read(ref _corrupt);
        public bool FirstWasKeyframe => Volatile.Read(ref _firstWasKeyframe) == 1;

        private void OnFrame(AssembledFrame frame)
        {
            if (Interlocked.Increment(ref _delivered) == 1)
                Volatile.Write(ref _firstWasKeyframe, frame.Keyframe ? 1 : 0);
            if (!TestPattern.Verify(frame.Data, frame.Keyframe))
                Interlocked.Increment(ref _corrupt);
        }

        public async Task WaitForFrames(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (Delivered < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Only {Delivered} of {count} frames arrived. Client: {Client.Stats}");
                await Task.Delay(20);
            }
        }

        public void Dispose()
        {
            _keepAlive.Dispose();
            Client.Dispose();
            Streamer.Dispose();
        }
    }

    [Fact]
    public async Task Frames_arrive_intact_starting_with_a_keyframe()
    {
        using var rig = new Rig();
        await rig.WaitForFrames(50);

        Assert.True(rig.FirstWasKeyframe);
        Assert.Equal(0, rig.Corrupt);
        Assert.Equal(0, rig.Client.Stats.Receive.FramesLost);
        Assert.Equal(1, rig.Streamer.Stats.Clients);
    }

    [Fact]
    public async Task A_lost_frame_is_recovered_with_a_requested_keyframe()
    {
        // Drop the frame 20 after the first keyframe sent: frames before it may be skipped
        // anyway while the client waits for its first keyframe. Send runs on one thread.
        uint? firstKeyframe = null;
        using var rig = new Rig(sender => new LossySender(sender, h =>
        {
            if (h.Keyframe)
                firstKeyframe ??= h.Frame;
            return firstKeyframe is { } first && h.Frame == first + 20;
        }));
        await rig.WaitForFrames(60);

        Assert.Equal(0, rig.Corrupt);
        Assert.True(rig.Client.Stats.FramesSkipped >= 1, "frames after the lost one must not be decoded");
        Assert.True(rig.Streamer.Stats.KeyframesSent >= 2, "the client's request must produce a new keyframe");
    }

    [Fact]
    public async Task Random_loss_is_repaired_without_corruption()
    {
        var rng = new Random(5);
        using var rig = new Rig(sender => new LossySender(sender, _ => rng.NextDouble() < 0.05));
        await rig.WaitForFrames(100);

        Assert.Equal(0, rig.Corrupt);
        Assert.True(rig.Client.Stats.Receive.ShardsRecovered > 0);
    }
}
