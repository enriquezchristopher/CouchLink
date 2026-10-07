namespace CouchLink.Core.Audio;

/// <summary>The one audio format CouchLink streams: Opus, 48 kHz stereo, 5 ms frames, 128 kbps.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;

    /// <summary>Samples per channel in one 5 ms frame.</summary>
    public const int FrameSamples = 240;

    /// <summary>16-bit values in one frame, interleaved left, right, left, ...</summary>
    public const int FrameValues = FrameSamples * Channels;

    public const int BitRate = 128_000;

    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(5);
}
