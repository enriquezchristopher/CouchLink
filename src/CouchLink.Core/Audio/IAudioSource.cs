namespace CouchLink.Core.Audio;

/// <summary>Where the host's 5 ms frames of 16-bit stereo PCM come from.</summary>
public interface IAudioSource : IDisposable
{
    /// <summary>For the status text: "process loopback", "device loopback" or "test tone".</summary>
    string Description { get; }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next frame and copies it into
    /// <paramref name="frame"/> (<see cref="AudioFormat.FrameValues"/> interleaved values).
    /// <paramref name="discontinuity"/> is true when audio before this frame is missing (capture
    /// started, restarted, or lost data), so the frame does not follow the previous one.
    /// </summary>
    bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity);
}
