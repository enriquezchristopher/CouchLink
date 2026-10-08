using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Client side: receives host announces on the discovery port.</summary>
public sealed class DiscoveryListener : IDisposable
{
    private readonly UdpClient _udp;

    private DiscoveryListener(UdpClient udp) => _udp = udp;

    /// <summary>Opens the port, or returns a message for the join list if it can't be opened.</summary>
    public static bool TryCreate(int port, out DiscoveryListener? listener, out string? error)
    {
        try
        {
            listener = new DiscoveryListener(new UdpClient(new IPEndPoint(IPAddress.Any, port)));
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            listener = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Can't search for hosts (port {port} in use)."
                : $"Can't search for hosts: {e.Message}";
            return false;
        }
    }

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    /// <summary>Receives until cancelled; anything that isn't an announce is ignored.</summary>
    public Task RunAsync(Action<HostAnnounce, IPAddress> onAnnounce, CancellationToken ct, Action<Exception>? onError = null) =>
        UdpReceiveLoop.RunAsync(_udp, result =>
        {
            if (HostAnnounce.TryParse(result.Buffer, out var announce))
                onAnnounce(announce, result.RemoteEndPoint.Address);
        }, ct, onError);

    public void Dispose() => _udp.Dispose();
}
