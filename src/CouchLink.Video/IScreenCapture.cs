using CouchLink.Core.Video;

namespace CouchLink.Video;

public enum CaptureStatus
{
    /// <summary>The screen changed; the new image is ready to encode.</summary>
    NewFrame,

    /// <summary>Nothing changed within the timeout; the last image is still current.</summary>
    NoChange,

    /// <summary>Capture is unavailable (UAC prompt, display mode change, exclusive fullscreen).</summary>
    Lost,
}

/// <summary>The host screen. Not thread-safe; used from the video thread only.</summary>
public interface IScreenCapture : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Waits up to <paramref name="timeout"/> for a screen change. Returns Lost at once when capture is unavailable, retrying each call.</summary>
    CaptureStatus TryCapture(TimeSpan timeout);
}

/// <summary>Encodes the capture's current image to H.264 at a fixed size.</summary>
public interface IFrameEncoder : IDisposable
{
    string Name { get; }
    bool IsHardware { get; }
    VideoSize Size { get; }

    /// <summary>Encodes the current image. Returns false when the encoder produced no packet yet.</summary>
    bool Encode(bool forceKeyframe, out EncodedFrame frame);
}
