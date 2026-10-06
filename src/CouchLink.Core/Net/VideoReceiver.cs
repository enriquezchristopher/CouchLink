using System.Net;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>Client side: receives video datagrams (UDP 47802) and hands each one to a callback.</summary>
public sealed class VideoReceiver : IDisposable
{
    /// <summary>
    /// A 150 KB keyframe arrives as ~125-150 back-to-back datagrams; Windows' default 64 KB
    /// socket buffer would drop most of them.
    /// </summary>
    public const int ReceiveBufferBytes = 8 * 1024 * 1024;

    private readonly UdpClient _udp;

    public VideoReceiver(int port)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = ReceiveBufferBytes;
    }

    /// <summary>Opens the port, or returns a message for the user if it can't be opened.</summary>
    public static bool TryCreate(int port, out VideoReceiver? receiver, out string? error)
    {
        try
        {
            receiver = new VideoReceiver(port);
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            receiver = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"UDP port {port} is already in use. Is CouchLink already joined to a host on this PC?"
                : $"Could not open UDP port {port}: {e.Message}";
            return false;
        }
    }

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public int ReceiveBufferSize => _udp.Client.ReceiveBufferSize;

    public Task RunAsync(Action<byte[]> onDatagram, CancellationToken ct, Action<Exception>? onError = null) =>
        UdpReceiveLoop.RunAsync(_udp, result => onDatagram(result.Buffer), ct, onError);

    public void Dispose() => _udp.Dispose();
}
