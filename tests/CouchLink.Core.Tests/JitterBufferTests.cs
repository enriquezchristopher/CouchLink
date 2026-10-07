using CouchLink.Core.Audio;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class JitterBufferTests
{
    /// <summary>Plays <paramref name="count"/> frames: "12" a frame, "r12" a repaired copy, "c" concealed, "-" silence.</summary>
    private static string Play(JitterBuffer buffer, int count) =>
        string.Join(" ", Enumerable.Range(0, count).Select(_ => buffer.Next() switch
        {
            { Kind: PlayoutKind.Frame } p => p.Data.Span[0].ToString(),
            { Kind: PlayoutKind.Repaired } p => $"r{p.Data.Span[0]}",
            { Kind: PlayoutKind.Conceal } => "c",
            _ => "-",
        }));

    /// <summary>A buffer that has primed on 10, 11, 12 and plays 10 next.</summary>
    private static JitterBuffer Started()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));
        buffer.Add(Packet(12));
        return buffer;
    }

    [Fact]
    public void Waits_for_three_frames_before_playing()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));
        Assert.Equal("- -", Play(buffer, 2));
        Assert.False(buffer.Playing);

        buffer.Add(Packet(12));

        Assert.True(buffer.Playing);
        Assert.Equal("10 11 12 c", Play(buffer, 4));
    }

    [Fact]
    public void Plays_in_sequence_order_whatever_the_arrival_order()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(12));
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));

        Assert.Equal("10 11 12", Play(buffer, 3));
    }

    [Fact]
    public void One_lost_packet_is_rebuilt_from_the_copy_in_the_next()
    {
        var buffer = Started();
        buffer.Add(Packet(14)); // 13 was lost; 14 carries it

        Assert.Equal("10 11 12 r13 14", Play(buffer, 5));
        Assert.Equal(1, buffer.Stats.Repaired);
    }

    [Fact]
    public void A_frame_lost_with_its_copy_is_concealed()
    {
        var buffer = Started();
        buffer.Add(Packet(15)); // 13 and 14 lost; 15 carries 14 only

        Assert.Equal("10 11 12 c r14 15", Play(buffer, 6));
        Assert.Equal(1, buffer.Stats.Concealed);
    }

    [Fact]
    public void After_four_concealed_frames_it_plays_silence_and_primes_again()
    {
        var buffer = Started();
        Assert.Equal("10 11 12", Play(buffer, 3));

        Assert.Equal("c c c c -", Play(buffer, 5));
        Assert.False(buffer.Playing);
        Assert.Equal(1, buffer.Stats.Underruns);

        buffer.Add(Packet(20, withPrevious: false));
        buffer.Add(Packet(21));
        Assert.Equal("-", Play(buffer, 1));
        buffer.Add(Packet(22));
        Assert.Equal("20 21 22", Play(buffer, 3));
    }

    [Fact]
    public void Late_packets_are_dropped_and_counted()
    {
        var buffer = Started();
        Assert.Equal("10 11 12 c", Play(buffer, 4)); // 13 concealed

        buffer.Add(Packet(13)); // too late: concealed already
        buffer.Add(Packet(11)); // too late: played already

        Assert.Equal(2, buffer.Stats.Late);
        Assert.Equal("c", Play(buffer, 1)); // 14 is still missing; 13 was not queued
    }

    [Fact]
    public void A_late_burst_after_a_hiccup_resumes_at_once()
    {
        var buffer = Started();
        Assert.Equal("10 11 12 c c", Play(buffer, 5)); // a hiccup: 13 and 14 concealed

        foreach (uint s in new uint[] { 13, 14, 15, 16, 17 })
            buffer.Add(Packet(s)); // the delayed burst

        Assert.True(buffer.Playing);
        Assert.Equal("15 16 17", Play(buffer, 3));
        Assert.Equal(2, buffer.Stats.Late);
    }

    [Fact]
    public void A_new_stream_id_starts_over()
    {
        var buffer = Started();
        Assert.Equal("10", Play(buffer, 1));

        Assert.True(buffer.Add(Packet(100, withPrevious: false, stream: 2)));
        Assert.False(buffer.Playing);
        Assert.Equal("-", Play(buffer, 1));
        Assert.False(buffer.Add(Packet(101, stream: 2)));
        buffer.Add(Packet(102, stream: 2));

        Assert.Equal("100 101 102", Play(buffer, 3));
    }

    [Fact]
    public void More_than_60_ms_queued_is_cut_to_15_ms()
    {
        var buffer = Started();
        Assert.Equal("10", Play(buffer, 1)); // 11 is next

        for (uint s = 13; s <= 23; s++)
            buffer.Add(Packet(s)); // at 23, 13 frames would be queued

        Assert.Equal(3, buffer.Depth);
        Assert.Equal(10, buffer.Stats.Discarded); // 11 to 20
        Assert.Equal("21 22 23", Play(buffer, 3));
    }

    [Fact]
    public void Depth_counts_from_the_next_frame_to_the_newest()
    {
        var buffer = Started();
        Assert.Equal(3, buffer.Depth);

        Play(buffer, 1);
        Assert.Equal(2, buffer.Depth);

        buffer.Add(Packet(14));
        Assert.Equal(4, buffer.Depth); // 11, 12, 13 (from 14's copy), 14
    }

    [Fact]
    public void Sequence_numbers_wrap_around()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(uint.MaxValue - 1, withPrevious: false));
        buffer.Add(Packet(uint.MaxValue));
        buffer.Add(Packet(0));

        Assert.Equal("254 255 0", Play(buffer, 3));
    }

    [Fact]
    public void A_duplicate_packet_plays_once()
    {
        var buffer = Started();
        buffer.Add(Packet(11));

        Assert.Equal("10 11 12 c", Play(buffer, 4));
    }
}
