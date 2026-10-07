using System.Diagnostics;

namespace CouchLink.Core.Audio;

/// <summary>
/// --test-tone: a 440 Hz beep, <see cref="BeepLength"/> on in every <see cref="Period"/>, produced
/// in real time, so gaps and clicks are easy to hear without a game.
/// </summary>
public sealed class TestToneSource : IAudioSource
{
    public const int Frequency = 440;
    public const short Amplitude = 8000;
    public static readonly TimeSpan BeepLength = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private static readonly long BeepSamples = (long)(BeepLength.TotalSeconds * AudioFormat.SampleRate);
    private static readonly long PeriodSamples = (long)(Period.TotalSeconds * AudioFormat.SampleRate);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _frames;

    public string Description => "test tone";

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        var wait = AudioFormat.FrameDuration * _frames - _clock.Elapsed;
        if (wait > timeout)
        {
            Thread.Sleep(timeout);
            return false;
        }
        if (wait > TimeSpan.Zero)
            Thread.Sleep(wait);

        discontinuity = _frames == 0;
        Fill(frame, _frames++);
        return true;
    }

    /// <summary>Writes frame number <paramref name="index"/> of the tone, the same on both channels.</summary>
    public static void Fill(Span<short> frame, long index)
    {
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            long n = index * AudioFormat.FrameSamples + i;
            short value = n % PeriodSamples < BeepSamples
                ? (short)(Amplitude * Math.Sin(2 * Math.PI * Frequency * n / AudioFormat.SampleRate))
                : (short)0;
            frame[2 * i] = value;
            frame[2 * i + 1] = value;
        }
    }

    public void Dispose()
    {
    }
}
