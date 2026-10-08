using System.Net;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: the clients to stream to (slot -> address), set by the session as clients are let in,
/// leave, are kicked or go silent. Two slots on one PC get one copy. Not thread-safe.
/// </summary>
public sealed class StreamTargets(int port)
{
    private readonly Dictionary<byte, IPEndPoint> _targets = [];

    public void Add(byte slot, IPAddress address) => _targets[slot] = new IPEndPoint(address, port);

    public bool Remove(byte slot) => _targets.Remove(slot);

    /// <summary>One entry per endpoint.</summary>
    public IReadOnlyList<IPEndPoint> Current() => _targets.Values.Distinct().ToList();
}
