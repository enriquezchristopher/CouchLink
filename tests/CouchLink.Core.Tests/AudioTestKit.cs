using System.Collections.Concurrent;
using System.Net;
using CouchLink.Core.Audio;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

internal static class AudioTestKit
{
    /// <summary>
    /// A packet whose Opus "frame" is one byte, the sequence's low byte, so tests can tell frames
    /// apart. With <paramref name="withPrevious"/> it carries the previous frame the same way.
    /// </summary>
    public static AudioPacket Packet(uint sequence, bool withPrevious = true, ushort stream = 1)
    {
        ReadOnlyMemory<byte> previous = withPrevious ? new[] { (byte)(sequence - 1) } : ReadOnlyMemory<byte>.Empty;
        return new AudioPacket(stream, sequence, new[] { (byte)sequence }, previous);
    }

    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}

/// <summary>A source the test feeds: each frame is filled with one value.</summary>
internal sealed class QueueSource : IAudioSource
{
    private readonly BlockingCollection<(short Value, bool Discontinuity)> _frames = new();
    private int _taken;

    public string Description => "queue";

    public int Taken => Volatile.Read(ref _taken);

    public void Add(short value, bool discontinuity = false) => _frames.Add((value, discontinuity));

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        if (!_frames.TryTake(out var item, timeout))
            return false;
        frame.Fill(item.Value);
        discontinuity = item.Discontinuity;
        Interlocked.Increment(ref _taken);
        return true;
    }

    public void Dispose() => _frames.Dispose();
}

/// <summary>"Encodes" a frame as two bytes: its first sample's low byte, then 0xEE. Throws on a frame of 99s.</summary>
internal sealed class TinyEncoder : IAudioEncoder
{
    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
    {
        if (pcm[0] == 99)
            throw new InvalidOperationException("encoder broke");
        output[0] = (byte)pcm[0];
        output[1] = 0xEE;
        return 2;
    }

    public void Dispose() { }
}

internal sealed class AudioRecordingSender : IVideoPacketSender
{
    private readonly List<(byte[] Packet, IPEndPoint Target)> _sent = [];

    public int Count { get { lock (_sent) return _sent.Count; } }

    public List<(AudioPacket Packet, IPEndPoint Target)> Parsed()
    {
        lock (_sent)
            return _sent.Select(s =>
            {
                Assert.True(AudioPacket.TryParse(s.Packet, out var p));
                return (p, s.Target);
            }).ToList();
    }

    public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
    {
        lock (_sent)
            foreach (var p in packets)
                foreach (var t in targets)
                    _sent.Add((p, t));
    }

    public void Dispose() { }
}

/// <summary>
/// "Decodes" a frame into samples that all equal its first byte; conceals as -1; throws on a frame
/// longer than one byte (the corrupt-frame stand-in).
/// </summary>
internal sealed class FakeAudioDecoder : IAudioDecoder
{
    public int Decoded;
    public int Resets;

    public int Decode(ReadOnlySpan<byte> frame, Span<short> pcm)
    {
        if (frame.Length > 1)
            throw new InvalidDataException("corrupt frame");
        Decoded++;
        pcm.Fill(frame[0]);
        return AudioFormat.FrameSamples;
    }

    public void Conceal(Span<short> pcm) => pcm.Fill(-1);

    public void Reset() => Resets++;

    public void Dispose() { }
}
