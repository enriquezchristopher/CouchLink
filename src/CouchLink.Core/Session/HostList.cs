using System.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

/// <summary>A host on the join list. <see cref="Players"/> and <see cref="Capacity"/> count clients only (the host player is extra).</summary>
public readonly record struct FoundHost(string Name, IPAddress Address, int Players, int Capacity, bool Compatible);

/// <summary>
/// Client side: the hosts heard on the LAN, by name, so a host with two adapters (or a host on this
/// same PC, heard on loopback and on its LAN address) is listed once. A loopback address never
/// replaces a LAN one. A host is dropped after <see cref="ExpireAfter"/> without an announce.
/// Thread-safe.
/// </summary>
public sealed class HostList(TimeProvider time)
{
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, (FoundHost Host, long SeenAt)> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public void Seen(HostAnnounce announce, IPAddress from)
    {
        lock (_gate)
        {
            var address = from;
            if (IPAddress.IsLoopback(from)
                && _hosts.TryGetValue(announce.Name, out var known)
                && !IPAddress.IsLoopback(known.Host.Address))
                address = known.Host.Address;
            var host = new FoundHost(announce.Name, address, announce.Players, announce.Capacity, announce.Compatible);
            _hosts[announce.Name] = (host, time.GetTimestamp());
        }
    }

    public IReadOnlyList<FoundHost> Current()
    {
        lock (_gate)
        {
            foreach (var name in _hosts.Where(h => time.GetElapsedTime(h.Value.SeenAt) >= ExpireAfter).Select(h => h.Key).ToList())
                _hosts.Remove(name);
            return _hosts.Values.Select(h => h.Host).OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
