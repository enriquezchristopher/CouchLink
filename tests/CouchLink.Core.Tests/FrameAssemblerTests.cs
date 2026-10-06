using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class FrameAssemblerTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(1);

    /// <summary>A random frame and its packets (24 000 bytes = 20 data + 4 parity shards).</summary>
    private static (byte[] Frame, List<byte[]> Packets) Make(uint number, int length, bool keyframe = false, ushort stream = 1)
    {
        var frame = new byte[length];
        new Random((int)number + length).NextBytes(frame);
        return (frame, new FramePacketizer(stream).Packetize(number, frame, keyframe));
    }

    private static List<AssembledFrame> Feed(FrameAssembler assembler, IEnumerable<byte[]> packets, TimeSpan? now = null)
    {
        var done = new List<AssembledFrame>();
        foreach (var packet in packets)
            if (assembler.Add(packet, now ?? T0) is { } frame)
                done.Add(frame);
        return done;
    }

    private static byte[] Rewrite(byte[] packet, Func<VideoShardHeader, VideoShardHeader> change)
    {
        var copy = (byte[])packet.Clone();
        Assert.True(VideoShardPacket.TryParse(copy, out var header));
        VideoShardPacket.WriteHeader(copy, change(header));
        return copy;
    }

    [Fact]
    public void Complete_frame_is_delivered_once_with_the_right_bytes()
    {
        var (frame, packets) = Make(1, 24_000, keyframe: true);
        var a = new FrameAssembler();

        var done = Assert.Single(Feed(a, packets));

        Assert.Equal(1u, done.Number);
        Assert.True(done.Keyframe);
        Assert.Equal(frame, done.Data);
        Assert.Equal(new VideoReceiveStats(
            PacketsReceived: 24, FramesCompleted: 1, FramesLost: 0,
            DataShardsExpected: 20, DataShardsMissing: 0, ShardsRecovered: 0), a.Stats);
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Packets_in_any_order_give_the_same_frame()
    {
        var (frame, packets) = Make(1, 24_000);
        packets.Reverse();
        Assert.Equal(frame, Assert.Single(Feed(new FrameAssembler(), packets)).Data);
    }

    [Fact]
    public void Lost_data_shards_are_repaired_up_to_the_parity_count()
    {
        var (frame, packets) = Make(1, 24_000); // 20 data + 4 parity
        var a = new FrameAssembler();
        var kept = packets.Where((_, i) => i is not (0 or 5 or 10 or 19)).ToList();

        Assert.Equal(frame, Assert.Single(Feed(a, kept)).Data);
        Assert.Equal(4, a.Stats.ShardsRecovered);
        Assert.Equal(4, a.Stats.DataShardsMissing);
        Assert.Equal(20.0, a.Stats.LossPercent);
    }

    [Fact]
    public void Multi_block_keyframe_is_repaired_in_every_block()
    {
        var (frame, packets) = Make(1, 300_000, keyframe: true); // 250 data shards: 2 blocks of 125 + 25
        var a = new FrameAssembler();
        var kept = packets.Where((_, i) => i % 7 != 3).ToList(); // 21 and 22 packets lost per block

        Assert.Equal(frame, Assert.Single(Feed(a, kept)).Data);
        Assert.True(a.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Frame_with_too_many_losses_is_given_up_when_a_newer_frame_completes()
    {
        var (_, first) = Make(1, 24_000);
        var (second, next) = Make(2, 24_000);
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Assert.Empty(Feed(a, first.Skip(5))); // 5 data shards missing, only 4 parity
        Assert.Empty(lost);
        Assert.Equal(second, Assert.Single(Feed(a, next)).Data);

        Assert.Equal(new[] { 1u }, lost);
        Assert.Equal(1, a.Stats.FramesLost);
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Duplicates_and_packets_of_finished_frames_are_ignored()
    {
        var (_, first) = Make(1, 24_000);
        var (_, second) = Make(2, 24_000);
        var a = new FrameAssembler();

        Assert.Single(Feed(a, second.Concat(second)));
        Assert.Empty(Feed(a, first)); // older than the delivered frame 2
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Unfinished_frame_is_given_up_after_the_timeout()
    {
        var (_, packets) = Make(1, 24_000);
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Feed(a, packets.Take(10), T0);
        a.AbandonStale(T0 + FrameAssembler.FrameTimeout - TimeSpan.FromMilliseconds(1));
        Assert.Empty(lost);
        a.AbandonStale(T0 + FrameAssembler.FrameTimeout);
        Assert.Equal(new[] { 1u }, lost);

        Assert.Empty(Feed(a, packets.Skip(10), T0 + FrameAssembler.FrameTimeout)); // too late now
    }

    [Fact]
    public void At_most_MaxPendingFrames_unfinished_frames_are_kept()
    {
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        for (uint n = 1; n <= FrameAssembler.MaxPendingFrames + 1; n++)
            Feed(a, Make(n, 24_000).Packets.Take(1));

        Assert.Equal(FrameAssembler.MaxPendingFrames, a.PendingFrames);
        Assert.Equal(new[] { 1u }, lost);
    }

    [Fact]
    public void Packets_that_disagree_with_their_frame_are_ignored()
    {
        var (frame, packets) = Make(1, 24_000);
        var a = new FrameAssembler();

        Feed(a, packets.Take(1));
        Assert.Empty(Feed(a,
        [
            Rewrite(packets[1], h => h with { FrameLength = 99 }),
            Rewrite(packets[2], h => h with { DataShards = 19 }),
            Rewrite(packets[3], h => h with { Keyframe = true }),
        ]));

        Assert.Equal(frame, Assert.Single(Feed(a, packets.Skip(1))).Data);
    }

    [Fact]
    public void Packets_whose_length_does_not_fit_their_shards_are_ignored()
    {
        var (_, packets) = Make(1, 100); // 1 data shard can't hold 5000 bytes
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Assert.Empty(Feed(a, packets.Select(p => Rewrite(p, h => h with { FrameLength = 5_000 }))));
        Assert.Empty(lost);              // rejected by the parser, so no frame was ever started
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Frame_numbers_wrap_around()
    {
        var a = new FrameAssembler();
        Assert.Single(Feed(a, Make(uint.MaxValue, 5_000).Packets));
        Assert.Single(Feed(a, Make(0, 5_000).Packets));
        Assert.Equal(0, a.Stats.FramesLost);
    }

    [Fact]
    public void A_new_stream_id_starts_over_even_with_lower_frame_numbers()
    {
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);
        Assert.Single(Feed(a, Make(50_000, 5_000, stream: 1).Packets));
        Feed(a, Make(50_001, 5_000, stream: 1).Packets.Take(1)); // unfinished frame of the old stream

        var first = Assert.Single(Feed(a, Make(0, 5_000, keyframe: true, stream: 2).Packets));

        Assert.Equal(0u, first.Number);
        Assert.Empty(lost);
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Paused_frames_arrive_marked_and_packets_must_agree()
    {
        var frame = new byte[5_000];
        var packets = new FramePacketizer(1).Packetize(1, frame, keyframe: false, paused: true);
        var a = new FrameAssembler();

        Feed(a, packets.Take(1));
        Assert.Empty(Feed(a, [Rewrite(packets[1], h => h with { Paused = false })])); // disagrees: ignored
        Assert.True(Assert.Single(Feed(a, packets.Skip(1))).Paused);
    }

    [Fact]
    public void A_frame_reports_how_long_its_packets_took_to_arrive()
    {
        var packets = new FramePacketizer(streamId: 9).Packetize(0, new byte[5000], keyframe: true);
        var a = new FrameAssembler();
        AssembledFrame? done = null;
        for (int i = 0; i < packets.Count && done is null; i++)
            done = a.Add(packets[i], TimeSpan.FromMilliseconds(10 + i)); // 1 ms apart

        Assert.NotNull(done);
        Assert.Equal(TimeSpan.FromMilliseconds(4), done.AssemblyTime); // 5 data shards: packets 0-4
    }
}
