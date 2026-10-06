using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class FramePacketizerTests
{
    private const int Payload = VideoShardPacket.PayloadSize;

    private static byte[] RandomFrame(int length, int seed = 1)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static List<VideoShardHeader> Headers(List<byte[]> packets) =>
        packets.Select(p =>
        {
            Assert.True(VideoShardPacket.TryParse(p, out var h));
            return h;
        }).ToList();

    [Fact]
    public void Small_frame_is_one_data_shard_plus_one_parity()
    {
        var frame = RandomFrame(100);
        var packets = new FramePacketizer(streamId: 7).Packetize(42, frame, keyframe: true);
        var headers = Headers(packets);

        Assert.Equal(2, packets.Count);
        Assert.All(headers, h =>
        {
            Assert.Equal((ushort)7, h.StreamId);
            Assert.Equal(42u, h.Frame);
            Assert.True(h.Keyframe);
            Assert.Equal(100u, h.FrameLength);
            Assert.Equal(1, h.BlockCount);
            Assert.Equal(1, h.DataShards);
            Assert.Equal(1, h.ParityShards);
        });
        Assert.Equal(new byte[] { 0, 1 }, headers.Select(h => h.Shard).ToArray());

        var payload = VideoShardPacket.Payload(packets[0]);
        Assert.True(payload[..100].SequenceEqual(frame));
        Assert.True(payload[100..].IndexOfAnyExcept((byte)0) < 0, "padding must be zero");
    }

    [Theory]
    [InlineData(1200, 1, 1)]
    [InlineData(1201, 2, 1)]
    [InlineData(12_000, 10, 2)]
    [InlineData(24_000, 20, 4)]
    [InlineData(240_000, 200, 40)]
    public void Shard_counts_at_20_percent(int length, int data, int parity)
    {
        var headers = Headers(new FramePacketizer(1).Packetize(0, RandomFrame(length), false));
        Assert.Equal(data + parity, headers.Count);
        Assert.All(headers, h =>
        {
            Assert.Equal(data, h.DataShards);
            Assert.Equal(parity, h.ParityShards);
            Assert.Equal(1, h.BlockCount);
        });
    }

    [Fact]
    public void Parity_count_rounds_up()
    {
        Assert.Equal(1, FramePacketizer.ParityShardsFor(1, 10));
        Assert.Equal(1, FramePacketizer.ParityShardsFor(10, 10));
        Assert.Equal(2, FramePacketizer.ParityShardsFor(11, 10));
        Assert.Equal(21, FramePacketizer.ParityShardsFor(101, 20));
        Assert.Equal(40, FramePacketizer.ParityShardsFor(200, 20));
    }

    [Fact]
    public void Large_frame_is_split_into_even_blocks_in_order()
    {
        var headers = Headers(new FramePacketizer(1).Packetize(9, RandomFrame(201 * Payload), true));
        var blocks = headers.GroupBy(h => h.Block).OrderBy(g => g.Key).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.All(headers, h => Assert.Equal(2, h.BlockCount));
        Assert.Equal(101, blocks[0].First().DataShards);
        Assert.Equal(21, blocks[0].First().ParityShards);
        Assert.Equal(100, blocks[1].First().DataShards);
        Assert.Equal(20, blocks[1].First().ParityShards);
        var expectedShards = Enumerable.Range(0, 122).Concat(Enumerable.Range(0, 120)).Select(i => (byte)i);
        Assert.Equal(expectedShards, headers.Select(h => h.Shard)); // block by block, data before parity
    }

    [Fact]
    public void Data_shards_in_order_give_back_the_frame()
    {
        var frame = RandomFrame(250_000, seed: 3);
        var packets = new FramePacketizer(1).Packetize(1, frame, true);
        Assert.All(packets, p => Assert.Equal(VideoShardPacket.Size, p.Length));

        var data = packets
            .Where(p => VideoShardPacket.TryParse(p, out var h) && h.Shard < h.DataShards)
            .SelectMany(p => VideoShardPacket.Payload(p).ToArray())
            .Take(frame.Length)
            .ToArray();
        Assert.Equal(frame, data);
    }

    [Fact]
    public void Parity_rebuilds_a_lost_data_shard()
    {
        var packets = new FramePacketizer(1).Packetize(1, RandomFrame(12_000), false); // 10 data + 2 parity
        var shards = packets.Select(p => (Memory<byte>)VideoShardPacket.Payload(p).ToArray()).ToArray();
        var original = shards[3].ToArray();
        shards[3].Span.Clear();
        var present = Enumerable.Range(0, 12).Select(i => i != 3).ToArray();

        Assert.True(ReedSolomon.For(10, 2).Reconstruct(shards, present, out int rebuilt));
        Assert.Equal(1, rebuilt);
        Assert.Equal(original, shards[3].ToArray());
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public void Parity_outside_10_to_20_percent_is_rejected(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FramePacketizer(1, percent));
    }

    [Fact]
    public void Empty_frame_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FramePacketizer(1).Packetize(0, Array.Empty<byte>(), false));
    }

    [Fact]
    public void Frame_over_the_size_limit_is_rejected()
    {
        var frame = new byte[VideoShardPacket.MaxFrameLength + 1];
        Assert.Throws<ArgumentException>(() => new FramePacketizer(1).Packetize(0, frame, true));
    }

    [Fact]
    public void Every_shape_the_packetizer_makes_parses_at_10_and_20_percent()
    {
        foreach (int percent in new[] { 10, 20 })
            foreach (int length in new[] { 1, 1200, 1201, 239_999, 240_000, 240_001, 1_000_000, (int)VideoShardPacket.MaxFrameLength })
                Assert.All(new FramePacketizer(1, percent).Packetize(0, new byte[length], false),
                    p => Assert.True(VideoShardPacket.TryParse(p, out _), $"{length} bytes at {percent}%"));
    }

    [Fact]
    public void Paused_frames_are_marked_in_every_packet()
    {
        var headers = Headers(new FramePacketizer(1).Packetize(3, RandomFrame(5_000), keyframe: false, paused: true));
        Assert.All(headers, h => Assert.True(h.Paused));
        Assert.All(Headers(new FramePacketizer(1).Packetize(4, RandomFrame(5_000), false)), h => Assert.False(h.Paused));
    }
}
