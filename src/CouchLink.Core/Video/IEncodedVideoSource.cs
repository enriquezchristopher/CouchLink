namespace CouchLink.Core.Video;

/// <summary>One encoded frame (an H.264 access unit). Paused: the host's capture is lost and this repeats the last image.</summary>
public readonly record struct EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe, bool Paused = false);

/// <summary>Host side: where encoded frames come from. Called from the streamer's one thread.</summary>
public interface IEncodedVideoSource : IDisposable
{
    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next frame. With
    /// <paramref name="forceKeyframe"/> the frame must be a keyframe. Returns false if no frame
    /// was ready in time (e.g. the screen didn't change).
    /// </summary>
    bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame);
}
