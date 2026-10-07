using System.Net;
using CouchLink.Core.Audio;
using CouchLink.Core.Protocol;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class AudioStreamerTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.3");

    private readonly QueueSource _source = new();
    private readonly AudioRecordingSender _sender = new();

    private AudioStreamer Streamer(Action<Exception>? onError = null) =>
        new(_source, new TinyEncoder(), _sender, 47802, TimeProvider.System, onError);

    [Fact]
    public async Task Each_frame_goes_to_every_client_with_the_previous_frame_attached()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        streamer.ClientSeen(3, B);
        _source.Add(1);
        _source.Add(2);
        _source.Add(3);

        await Until(() => _sender.Count == 6);

        var sent = _sender.Parsed();
        Assert.Equal(
            new[] { new IPEndPoint(A, 47802), new IPEndPoint(B, 47802) }.OrderBy(e => e.ToString()),
            sent.Select(s => s.Target).Distinct().OrderBy(e => e.ToString()));
        var toA = sent.Where(s => s.Target.Address.Equals(A)).Select(s => s.Packet).ToList();
        Assert.Equal(new byte[] { 1, 0xEE }, toA[0].Frame.ToArray());
        Assert.True(toA[0].Previous.IsEmpty);
        Assert.Equal(toA[0].Sequence + 1, toA[1].Sequence);
        Assert.Equal(toA[0].Frame.ToArray(), toA[1].Previous.ToArray());
        Assert.Equal(toA[1].Frame.ToArray(), toA[2].Previous.ToArray());
        Assert.All(sent, s => Assert.Equal(streamer.StreamId, s.Packet.StreamId));
        Assert.NotEqual(0, streamer.StreamId);
    }

    [Fact]
    public async Task After_a_discontinuity_the_previous_frame_is_left_out_and_the_sequence_jumps()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        _source.Add(1);
        _source.Add(2, discontinuity: true);
        _source.Add(3);

        await Until(() => _sender.Count == 3);

        var p = _sender.Parsed().Select(s => s.Packet).ToList();
        Assert.Equal(p[0].Sequence + 1 + AudioStreamer.SequenceJump, p[1].Sequence);
        Assert.True(p[1].Previous.IsEmpty);
        Assert.Equal(p[1].Frame.ToArray(), p[2].Previous.ToArray());
    }

    [Fact]
    public async Task Nothing_is_sent_while_no_client_listens_and_the_next_packet_has_no_previous()
    {
        using var streamer = Streamer();
        _source.Add(1);
        _source.Add(2);
        await Until(() => streamer.Stats.FramesCaptured == 2);
        Assert.Equal(0, _sender.Count);

        streamer.ClientSeen(2, A);
        _source.Add(3);
        await Until(() => _sender.Count == 1);

        var p = Assert.Single(_sender.Parsed()).Packet;
        Assert.Equal(new byte[] { 3, 0xEE }, p.Frame.ToArray());
        Assert.True(p.Previous.IsEmpty);
    }

    [Fact]
    public async Task An_encoder_error_is_reported_and_streaming_carries_on()
    {
        var errors = new List<Exception>();
        using var streamer = Streamer(e => { lock (errors) errors.Add(e); });
        streamer.ClientSeen(2, A);
        _source.Add(1);
        _source.Add(99); // the encoder throws on this one
        _source.Add(3);

        await Until(() => _sender.Count == 2);

        lock (errors)
            Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        var p = _sender.Parsed().Select(s => s.Packet).ToList();
        Assert.Equal(new byte[] { 3, 0xEE }, p[1].Frame.ToArray());
        Assert.True(p[1].Previous.IsEmpty); // frame 99 never went out, so 3 doesn't follow 1
    }

    [Fact]
    public async Task Stats_count_frames_packets_bytes_and_clients()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        streamer.ClientSeen(3, B);
        _source.Add(1);

        await Until(() => streamer.Stats.PacketsSent == 1);

        var s = streamer.Stats;
        Assert.Equal(1, s.FramesCaptured);
        Assert.Equal(AudioPacket.HeaderSize + 2, s.BytesSent); // one packet, sent to both
        Assert.Equal(2, s.Clients);
        Assert.Equal("queue", streamer.SourceDescription);
    }
}
