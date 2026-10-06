using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class VideoPacketTests
{
    // 300 000 bytes = 250 data shards = 2 blocks of 125, each with 25 parity (20%).
    private static readonly VideoShardHeader Sample = new(
        StreamId: 0xBEEF, Frame: 123_456, Keyframe: true, FrameLength: 300_000,
        Block: 1, BlockCount: 2, Shard: 7, DataShards: 125, ParityShards: 25);

    // The largest frame a client accepts: 3496 data shards = 18 blocks; block 1 has 195.
    private static readonly VideoShardHeader Largest = Sample with
    {
        FrameLength = VideoShardPacket.MaxFrameLength, BlockCount = 18, DataShards = 195, ParityShards = 39,
    };

    private static byte[] Packet(VideoShardHeader header)
    {
        var packet = new byte[VideoShardPacket.Size];
        VideoShardPacket.WriteHeader(packet, header);
        return packet;
    }

    [Fact]
    public void Header_round_trips()
    {
        Assert.True(VideoShardPacket.TryParse(Packet(Sample), out var parsed));
        Assert.Equal(Sample, parsed);
        Assert.True(VideoShardPacket.TryParse(Packet(Sample with { Keyframe = false }), out parsed));
        Assert.False(parsed.Keyframe);
    }

    [Fact]
    public void The_largest_allowed_frame_parses()
    {
        Assert.True(VideoShardPacket.TryParse(Packet(Largest), out var parsed));
        Assert.Equal(Largest, parsed);
    }

    [Fact]
    public void Payload_follows_the_header()
    {
        var packet = Packet(Sample);
        packet[VideoShardPacket.HeaderSize] = 0xAB;
        packet[^1] = 0xCD;
        var payload = VideoShardPacket.Payload(packet);
        Assert.Equal(VideoShardPacket.PayloadSize, payload.Length);
        Assert.Equal(0xAB, payload[0]);
        Assert.Equal(0xCD, payload[^1]);
    }

    [Theory]
    [InlineData(VideoShardPacket.Size - 1)]
    [InlineData(VideoShardPacket.Size + 1)]
    [InlineData(InputPacket.Size)]
    public void Wrong_length_is_rejected(int length)
    {
        var packet = new byte[length];
        Packet(Sample).AsSpan(0, Math.Min(length, VideoShardPacket.Size)).CopyTo(packet);
        Assert.False(VideoShardPacket.TryParse(packet, out _));
    }

    [Fact]
    public void Wrong_magic_version_or_type_is_rejected()
    {
        foreach (int offset in new[] { 0, 2, 3 })
        {
            var packet = Packet(Sample);
            packet[offset] ^= 0xFF;
            Assert.False(VideoShardPacket.TryParse(packet, out _), $"byte {offset} changed");
        }
    }

    [Theory]
    [InlineData("no blocks")]
    [InlineData("block past the end")]
    [InlineData("no data shards")]
    [InlineData("shard past the end")]
    [InlineData("empty frame")]
    [InlineData("too many shards for Reed-Solomon")]
    [InlineData("a block of more than 200 data shards")]
    [InlineData("block count that doesn't match the length")]
    [InlineData("data shards that don't match the length")]
    [InlineData("less than 10% parity")]
    [InlineData("more than 20% parity")]
    [InlineData("frame over the size limit")]
    public void Impossible_headers_are_rejected(string problem)
    {
        var header = problem switch
        {
            "a block of more than 200 data shards" => Sample with
            {
                FrameLength = 201 * 1200, Block = 0, BlockCount = 1, DataShards = 201, ParityShards = 41,
            },
            "block count that doesn't match the length" => Sample with { BlockCount = 3 },
            "data shards that don't match the length" => Sample with { DataShards = 124 },
            "less than 10% parity" => Sample with { ParityShards = 12 },
            "more than 20% parity" => Sample with { ParityShards = 26 },
            "frame over the size limit" => Largest with { FrameLength = VideoShardPacket.MaxFrameLength + 1 },
            "no blocks" => Sample with { Block = 0, BlockCount = 0 },
            "block past the end" => Sample with { Block = 2 },
            "no data shards" => Sample with { DataShards = 0 },
            "shard past the end" => Sample with { Shard = 150 }, // 125 + 25 shards: 0..149
            "empty frame" => Sample with { FrameLength = 0 },
            "too many shards for Reed-Solomon" => Sample with { DataShards = 250, ParityShards = 10 },
            _ => throw new ArgumentException(problem),
        };
        Assert.False(VideoShardPacket.TryParse(Packet(header), out _));
    }

    [Fact]
    public void Keyframe_request_round_trips_and_is_no_other_packet()
    {
        var bytes = new byte[KeyframeRequest.Size];
        new KeyframeRequest(Slot: 5).WriteTo(bytes);
        Assert.True(KeyframeRequest.TryParse(bytes, out var request));
        Assert.Equal((byte)5, request.Slot);
        Assert.False(InputPacket.TryParse(bytes, out _));
        Assert.False(VideoShardPacket.TryParse(bytes, out _));
    }

    [Fact]
    public void Input_packet_is_not_a_keyframe_request()
    {
        var bytes = new byte[InputPacket.Size];
        new InputPacket(2, 1, 1, PadState.Neutral).WriteTo(bytes);
        Assert.False(KeyframeRequest.TryParse(bytes, out _));
        Assert.False(KeyframeRequest.TryParse(bytes.AsSpan(0, KeyframeRequest.Size), out _)); // right size, wrong type
    }
}
