namespace CouchLink.Core.Video;

/// <summary>
/// Host side: when to force a keyframe. A request (a client joined, or lost a frame) stays
/// pending until the encoder actually produces a keyframe, but keyframes are at least
/// <see cref="MinInterval"/> apart, so nine clients asking at once cost one keyframe.
/// Not thread-safe.
/// </summary>
public sealed class KeyframePolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private bool _pending;
    private TimeSpan? _lastKeyframe;

    public void Request() => _pending = true;

    public bool ShouldForce(TimeSpan now) =>
        _pending && (_lastKeyframe is not { } last || now - last >= MinInterval);

    /// <summary>Call for every keyframe sent, requested or not.</summary>
    public void KeyframeSent(TimeSpan now)
    {
        _pending = false;
        _lastKeyframe = now;
    }
}
