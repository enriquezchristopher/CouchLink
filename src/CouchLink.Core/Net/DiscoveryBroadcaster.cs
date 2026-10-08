using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// Host side: sends <see cref="HostAnnounce"/> at once and then every second to every broadcast
/// address (re-read every 10 s so a cable plugged in later is covered). Only sends; it never binds the
/// discovery port, so a client on the same PC can listen on it.
/// </summary>
public sealed class DiscoveryBroadcaster : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RefreshTargets = TimeSpan.FromSeconds(10);

    private readonly UdpClient _udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };
    private readonly int _port;
    private readonly Func<HostAnnounce> _announce;
    private readonly Func<IReadOnlyList<IPAddress>> _targets;
    private readonly Action<Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private IReadOnlyList<IPAddress> _current = [];
    private long? _refreshedAt;
    private bool _disposed;

    public DiscoveryBroadcaster(int port, Func<HostAnnounce> announce,
        Func<IReadOnlyList<IPAddress>>? targets = null, Action<Exception>? onError = null)
    {
        _port = port;
        _announce = announce;
        _targets = targets ?? BroadcastAddresses.Current;
        _onError = onError;
        _timer = new Timer(_ => Send(), null, TimeSpan.Zero, Interval);
    }

    private void Send()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                long now = Environment.TickCount64;
                if (_refreshedAt is not { } at || now - at >= RefreshTargets.TotalMilliseconds)
                {
                    _current = _targets();
                    _refreshedAt = now;
                }
                var bytes = _announce().ToArray();
                foreach (var address in _current)
                {
                    try
                    {
                        _udp.Send(bytes, bytes.Length, new IPEndPoint(address, _port));
                    }
                    catch (SocketException)
                    {
                        // An adapter that just went away; the next refresh drops it.
                    }
                }
            }
            catch (Exception e)
            {
                _onError?.Invoke(e); // never let a timer thread take the host down
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _timer.Dispose();
        _udp.Dispose();
    }
}
