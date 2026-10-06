using System.Net;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: the clients to stream to, learned from their input packets (slot -> address).
/// A client silent for <see cref="Timeout"/> is dropped. Until the v1.4 session channel this is
/// how the host knows who joined. Not thread-safe.
/// </summary>
public sealed class StreamTargets(int videoPort)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<byte, (IPEndPoint EndPoint, TimeSpan LastSeen)> _targets = [];

    /// <summary>Records that a client was heard from. Returns true if it is new, moved, or back after going silent, so it needs a keyframe.</summary>
    public bool Seen(byte slot, IPAddress address, TimeSpan now)
    {
        bool isNew = !_targets.TryGetValue(slot, out var known)
            || !known.EndPoint.Address.Equals(address)
            || now - known.LastSeen >= Timeout;
        _targets[slot] = (isNew ? new IPEndPoint(address, videoPort) : known.EndPoint, now);
        return isNew;
    }

    /// <summary>Clients heard from within <see cref="Timeout"/>, one entry per endpoint.</summary>
    public IReadOnlyList<IPEndPoint> Current(TimeSpan now)
    {
        foreach (var slot in _targets.Where(t => now - t.Value.LastSeen >= Timeout).Select(t => t.Key).ToList())
            _targets.Remove(slot);
        return _targets.Values.Select(t => t.EndPoint).Distinct().ToList();
    }
}
