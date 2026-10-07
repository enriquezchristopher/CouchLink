using Concentus;
using Concentus.Enums;
using CouchLink.Core.Audio;

namespace CouchLink.Audio;

/// <summary>Opus through Concentus (pure C#): 48 kHz stereo, low-delay (CELT) mode, 128 kbps, 5 ms frames.</summary>
public sealed class OpusAudioEncoder : IAudioEncoder
{
    private readonly IOpusEncoder _encoder;

    public OpusAudioEncoder()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false; // always the managed codec we ship and test
        _encoder = OpusCodecFactory.CreateEncoder(
            AudioFormat.SampleRate, AudioFormat.Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        _encoder.Bitrate = AudioFormat.BitRate;
    }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output) =>
        _encoder.Encode(pcm, AudioFormat.FrameSamples, output, output.Length);

    public void Dispose()
    {
    }
}
