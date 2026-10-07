using CouchLink.Core.Protocol;

namespace CouchLink.Core.Audio;

/// <summary>Decodes Opus frames into interleaved 16-bit stereo PCM.</summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>Decodes one frame; returns samples per channel. Throws on a corrupt frame.</summary>
    int Decode(ReadOnlySpan<byte> frame, Span<short> pcm);

    /// <summary>Makes up one frame to cover a lost one (packet loss concealment).</summary>
    void Conceal(Span<short> pcm);

    void Reset();
}

public readonly record struct AudioClientStats(
    long Packets,
    long Repaired,
    long Concealed,
    long Late,
    long Discarded,
    long Underruns,
    long DecodeErrors,
    long DriftCorrections,
    double BufferMs,
    bool Playing);

/// <summary>
/// Client side: audio datagrams in through <see cref="Receive"/> (the receive thread), PCM out
/// through <see cref="Read"/> (the sound device's thread). Each 5 ms of output comes from the
/// jitter buffer: the frame decoded, its redundant copy decoded, Opus concealment, or silence. A
/// corrupt frame is concealed and counted. Drift control drops or repeats the middle sample of a
/// frame when the buffer runs away from its starting depth. Takes ownership of the decoder.
/// </summary>
public sealed class AudioClient(IAudioDecoder decoder) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly JitterBuffer _buffer = new();
    private readonly DriftControl _drift = new();
    private readonly short[] _frame = new short[AudioFormat.FrameValues + AudioFormat.Channels]; // room to repeat one sample
    private int _frameLength, _frameRead; // device thread only
    private long _decodeErrors;

    public AudioClientStats Stats
    {
        get
        {
            lock (_lock)
            {
                var j = _buffer.Stats;
                return new AudioClientStats(j.Received, j.Repaired, j.Concealed, j.Late, j.Discarded, j.Underruns,
                    _decodeErrors, _drift.Corrections,
                    _buffer.Depth * AudioFormat.FrameDuration.TotalMilliseconds, _buffer.Playing);
            }
        }
    }

    /// <summary>An audio datagram from the host. Anything that isn't a valid audio packet is ignored.</summary>
    public void Receive(byte[] datagram)
    {
        if (!AudioPacket.TryParse(datagram, out var packet))
            return;
        lock (_lock)
        {
            if (_buffer.Add(packet))
            {
                decoder.Reset();
                _drift.Reset();
            }
        }
    }

    /// <summary>Fills all of <paramref name="output"/> (interleaved stereo) and returns its length.</summary>
    public int Read(Span<short> output)
    {
        int written = 0;
        while (written < output.Length)
        {
            if (_frameRead == _frameLength)
                NextFrame();
            int take = Math.Min(output.Length - written, _frameLength - _frameRead);
            _frame.AsSpan(_frameRead, take).CopyTo(output[written..]);
            _frameRead += take;
            written += take;
        }
        return output.Length;
    }

    private void NextFrame()
    {
        _frameRead = 0;
        _frameLength = AudioFormat.FrameValues;
        var pcm = _frame.AsSpan(0, AudioFormat.FrameValues);
        lock (_lock)
        {
            int depth = _buffer.Depth;
            var playout = _buffer.Next();
            switch (playout.Kind)
            {
                case PlayoutKind.Frame or PlayoutKind.Repaired:
                    try
                    {
                        decoder.Decode(playout.Data.Span, pcm);
                    }
                    catch (Exception)
                    {
                        _decodeErrors++; // counted, not logged: a bad stream would flood the log
                        decoder.Conceal(pcm);
                    }
                    break;
                case PlayoutKind.Conceal:
                    decoder.Conceal(pcm);
                    break;
                default:
                    pcm.Clear();
                    _drift.Reset();
                    return;
            }
            AdjustForDrift(_drift.Next(depth));
        }
    }

    /// <summary>Drops (-1) or repeats (+1) the middle sample of the current frame.</summary>
    private void AdjustForDrift(int correction)
    {
        const int middle = AudioFormat.FrameValues / 2; // a sample boundary: left channel
        if (correction < 0)
            _frame.AsSpan(middle + AudioFormat.Channels, AudioFormat.FrameValues - middle - AudioFormat.Channels)
                .CopyTo(_frame.AsSpan(middle));
        else if (correction > 0)
            _frame.AsSpan(middle, AudioFormat.FrameValues - middle).CopyTo(_frame.AsSpan(middle + AudioFormat.Channels));
        _frameLength = AudioFormat.FrameValues + correction * AudioFormat.Channels;
    }

    public void Dispose() => decoder.Dispose();
}
