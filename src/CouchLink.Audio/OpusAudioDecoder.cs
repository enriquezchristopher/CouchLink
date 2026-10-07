using Concentus;
using CouchLink.Core.Audio;

namespace CouchLink.Audio;

/// <summary>Opus through Concentus (pure C#); <see cref="Conceal"/> is Opus packet loss concealment.</summary>
public sealed class OpusAudioDecoder : IAudioDecoder
{
    private readonly IOpusDecoder _decoder;

    public OpusAudioDecoder()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        _decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, AudioFormat.Channels);
    }

    /// <summary>Throws Concentus's OpusException on a corrupt frame.</summary>
    public int Decode(ReadOnlySpan<byte> frame, Span<short> pcm) =>
        _decoder.Decode(frame, pcm, AudioFormat.FrameSamples, false);

    public void Conceal(Span<short> pcm) =>
        _decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, AudioFormat.FrameSamples, false);

    public void Reset() => _decoder.ResetState();

    public void Dispose()
    {
    }
}
