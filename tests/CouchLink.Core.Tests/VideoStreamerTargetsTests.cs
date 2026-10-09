using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class VideoStreamerTargetsTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.3");

    private sealed class RecordingSender : IVideoPacketSender
    {
        private readonly List<IPEndPoint> _targets = [];

        public int Count { get { lock (_targets) return _targets.Count; } }

        public int SentTo(IPAddress address)
        {
            lock (_targets)
                return _targets.Count(t => t.Address.Equals(address));
        }

        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
        {
            lock (_targets)
                foreach (var _ in packets)
                    _targets.AddRange(targets);
        }

        public void Dispose() { }
    }

    /// <summary>A frame every 5 ms; counts the frames the streamer asked to be keyframes.</summary>
    private sealed class CountingSource : IEncodedVideoSource
    {
        private int _forced;

        public int Forced => Volatile.Read(ref _forced);

        public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
        {
            Thread.Sleep(5);
            if (forceKeyframe)
                Interlocked.Increment(ref _forced);
            frame = new EncodedFrame(new byte[100], forceKeyframe);
            return true;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task Nothing_is_sent_until_a_target_is_added_and_adding_one_forces_a_keyframe()
    {
        var source = new CountingSource();
        var sender = new RecordingSender();
        using var streamer = new VideoStreamer(source, sender, 47802, TimeProvider.System);
        await Task.Delay(100);
        Assert.Equal(0, sender.Count);
        int forcedBefore = source.Forced;

        streamer.AddTarget(2, A);

        await Until(() => sender.SentTo(A) > 0);
        await Until(() => source.Forced > forcedBefore);
    }
}
