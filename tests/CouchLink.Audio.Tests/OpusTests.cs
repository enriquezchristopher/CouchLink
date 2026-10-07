using CouchLink.Core.Audio;
using CouchLink.Core.Protocol;

namespace CouchLink.Audio.Tests;

public class OpusTests
{
    private static short[] Sine(int frame, int hz = 1000, short amplitude = 8000)
    {
        var pcm = new short[AudioFormat.FrameValues];
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            long n = (long)frame * AudioFormat.FrameSamples + i;
            var value = (short)(amplitude * Math.Sin(2 * Math.PI * hz * n / AudioFormat.SampleRate));
            pcm[2 * i] = value;
            pcm[2 * i + 1] = value;
        }
        return pcm;
    }

    [Fact]
    public void A_1_kHz_sine_keeps_its_level_and_pitch()
    {
        using var encoder = new OpusAudioEncoder();
        using var decoder = new OpusAudioDecoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];
        var left = new List<short>();

        for (int f = 0; f < 200; f++)
        {
            int length = encoder.Encode(Sine(f), packet);
            var pcm = new short[AudioFormat.FrameValues];
            Assert.Equal(AudioFormat.FrameSamples, decoder.Decode(packet.AsSpan(0, length), pcm));
            if (f >= 10) // after the codec settles
                left.AddRange(pcm.Where((_, i) => i % 2 == 0));
        }

        double rms = Math.Sqrt(left.Average(v => (double)v * v));
        Assert.InRange(rms, 8000 / Math.Sqrt(2) * 0.8, 8000 / Math.Sqrt(2) * 1.2);
        int crossings = left.Zip(left.Skip(1)).Count(p => (p.First < 0) != (p.Second < 0));
        Assert.InRange(crossings, 1840, 1960); // 190 frames = 950 ms of 1 kHz = 1900 crossings
    }

    [Fact]
    public void Frames_are_about_80_bytes_and_fit_a_packet()
    {
        using var encoder = new OpusAudioEncoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];

        var sizes = Enumerable.Range(0, 200).Select(f => encoder.Encode(Sine(f, hz: 440 + f), packet)).ToList();

        Assert.All(sizes, s => Assert.InRange(s, 1, AudioPacket.MaxFrameBytes));
        Assert.InRange(sizes.Average(), 60, 110);
    }

    [Fact]
    public void Concealment_carries_the_sound_on_for_a_full_frame()
    {
        using var encoder = new OpusAudioEncoder();
        using var decoder = new OpusAudioDecoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];
        var pcm = new short[AudioFormat.FrameValues];
        for (int f = 0; f < 20; f++)
            decoder.Decode(packet.AsSpan(0, encoder.Encode(Sine(f), packet)), pcm);

        Array.Clear(pcm);
        decoder.Conceal(pcm);

        Assert.Contains(pcm, v => v != 0);
    }

    [Fact]
    public void A_corrupt_frame_throws_so_the_client_conceals_it()
    {
        using var decoder = new OpusAudioDecoder();

        Assert.ThrowsAny<Exception>(() => decoder.Decode(new byte[] { 0xFF, 0xFF, 0x01 }, new short[AudioFormat.FrameValues]));
    }
}
