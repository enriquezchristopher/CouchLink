using CouchLink.Core.Audio;
using CouchLink.Core.Protocol;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class AudioClientTests
{
    private const int Frame = AudioFormat.FrameValues;

    private readonly FakeAudioDecoder _decoder = new();

    private static void Send(AudioClient client, params AudioPacket[] packets)
    {
        foreach (var p in packets)
            client.Receive(p.ToArray());
    }

    /// <summary>Reads <paramref name="frames"/> frames' worth in device-sized chunks; one value per 5 ms ("mixed" if a block isn't uniform).</summary>
    private static string Hear(AudioClient client, int frames, int chunk = 256)
    {
        var all = new short[frames * Frame];
        for (int at = 0; at < all.Length; at += chunk)
            client.Read(all.AsSpan(at, Math.Min(chunk, all.Length - at)));
        return string.Join(" ", all.Chunk(Frame).Select(f => f.Distinct().Count() == 1 ? f[0].ToString() : "mixed"));
    }

    [Fact]
    public void Silence_until_three_frames_are_queued()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2));
        Assert.Equal("0", Hear(client, 1));

        Send(client, Packet(3));

        Assert.Equal("1 2 3", Hear(client, 3));
    }

    [Fact]
    public void Odd_device_chunks_lose_nothing()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(4), Packet(5), Packet(6));

        Assert.Equal("1 2 3 4 5 6", Hear(client, 6, chunk: 2 * 137));
    }

    [Fact]
    public void A_lost_packet_plays_its_copy_from_the_next()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(5)); // 4 lost

        Assert.Equal("1 2 3 4 5", Hear(client, 5));
        Assert.Equal(1, client.Stats.Repaired);
    }

    [Fact]
    public void A_frame_lost_everywhere_is_concealed()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(5, withPrevious: false), Packet(6));

        Assert.Equal("1 2 3 -1 5 6", Hear(client, 6));
        Assert.Equal(1, client.Stats.Concealed);
    }

    [Fact]
    public void A_corrupt_frame_is_concealed_and_counted()
    {
        using var client = new AudioClient(_decoder);
        Send(client,
            Packet(1, withPrevious: false),
            Packet(2),
            new AudioPacket(1, 3, new byte[] { 0xFF, 0xFF }, new byte[] { 2 }),
            Packet(4));

        Assert.Equal("1 2 -1 4", Hear(client, 4));
        Assert.Equal(1, client.Stats.DecodeErrors);
    }

    [Fact]
    public void A_new_stream_resets_the_decoder_and_starts_over()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3));
        Assert.Equal(1, _decoder.Resets);
        Assert.Equal("1", Hear(client, 1));

        Send(client, Packet(50, withPrevious: false, stream: 2));
        Assert.Equal(2, _decoder.Resets);
        Assert.Equal("0", Hear(client, 1));

        Send(client, Packet(51, stream: 2), Packet(52, stream: 2));
        Assert.Equal("50 51 52", Hear(client, 3));
    }

    [Fact]
    public void After_a_host_discontinuity_playback_restarts_cleanly()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(4));
        Assert.Equal("1 2 3 4", Hear(client, 4));
        Assert.Equal("-1 -1 -1 -1 0", Hear(client, 5)); // the host's capture restarts: concealment, then silence

        // The host's next packets: no previous frame, sequence jumped by 16.
        Send(client, Packet(5 + AudioStreamer.SequenceJump, withPrevious: false), Packet(22), Packet(23));

        Assert.Equal("21 22 23", Hear(client, 3));
        Assert.Equal(0, client.Stats.Late);
    }

    [Fact]
    public void A_buffer_that_stays_too_full_is_played_slightly_faster()
    {
        using var client = new AudioClient(_decoder);
        var frame = new short[Frame];
        uint next = 1;
        void SendOne() => client.Receive(Packet(next, withPrevious: next++ > 1).ToArray());

        for (int i = 0; i < 3; i++)
            SendOne(); // primed
        for (int i = 0; i < DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame); // 4 queued before each frame: the baseline
        }
        for (int i = 0; i < 3; i++)
            SendOne(); // 15 ms more
        for (int i = 0; i < DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame); // 7 queued: too full
        }
        double before = client.Stats.BufferMs;

        for (int i = 0; i < 2 * DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame);
        }

        Assert.True(client.Stats.DriftCorrections > 0);
        Assert.True(client.Stats.BufferMs < before, $"{client.Stats.BufferMs} ms should be below {before} ms");
    }

    [Fact]
    public void Stats_report_packets_and_buffer_depth()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3));

        var s = client.Stats;
        Assert.Equal(3, s.Packets);
        Assert.Equal(15, s.BufferMs);
        Assert.True(s.Playing);
    }

    [Fact]
    public void Junk_is_ignored()
    {
        using var client = new AudioClient(_decoder);
        client.Receive(new byte[] { 1, 2, 3 });

        Assert.Equal(0, client.Stats.Packets);
    }
}
