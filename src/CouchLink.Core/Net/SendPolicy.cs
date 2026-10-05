using CouchLink.Core.Input;

namespace CouchLink.Core.Net;

/// <summary>Send the full state on every change, and at least every 8 ms.</summary>
public sealed class SendPolicy
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromMilliseconds(8);

    private PadState _last;
    private TimeSpan? _lastSent;

    public bool ShouldSend(PadState state, TimeSpan now)
    {
        if (_lastSent is { } sent && state == _last && now - sent < MaxInterval)
            return false;

        _last = state;
        _lastSent = now;
        return true;
    }
}
