namespace CouchLink.Core.Video;

/// <summary>
/// Client side: decides which assembled frames may go to the decoder. An H.264 delta frame
/// needs the frame before it, so after any loss (a frame given up, or a gap in frame numbers)
/// the gate drops frames until a keyframe arrives. It asks the host for one at once and then
/// every <see cref="RequestInterval"/> until it comes. It starts out waiting, because a client
/// joining mid-stream needs a keyframe first. Not thread-safe.
/// </summary>
public sealed class DecodeGate
{
    public static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(300);

    private uint? _lastDelivered;
    private TimeSpan? _lastRequest;

    public bool WaitingForKeyframe { get; private set; } = true;
    public long FramesSkipped { get; private set; }
    public long KeyframeRequests { get; private set; }

    /// <summary>Returns true if the frame should be decoded.</summary>
    public bool Accept(AssembledFrame frame)
    {
        if (frame.Keyframe)
        {
            WaitingForKeyframe = false;
            _lastRequest = null;
        }
        else if (_lastDelivered is { } last && frame.Number != unchecked(last + 1))
        {
            WaitingForKeyframe = true; // a frame we never heard of is missing
        }

        if (WaitingForKeyframe)
        {
            FramesSkipped++;
            return false;
        }
        _lastDelivered = frame.Number;
        return true;
    }

    /// <summary>The assembler gave up on a frame; everything after it is undecodable until a keyframe.</summary>
    public void FrameLost(uint number)
    {
        if (_lastDelivered is { } last && unchecked((int)(number - last)) <= 0)
            return; // older than what we already showed
        WaitingForKeyframe = true;
    }

    /// <summary>True when a keyframe request should be sent now.</summary>
    public bool ShouldRequestKeyframe(TimeSpan now)
    {
        if (!WaitingForKeyframe)
            return false;
        if (_lastRequest is { } sent && now - sent < RequestInterval)
            return false;
        _lastRequest = now;
        KeyframeRequests++;
        return true;
    }
}
