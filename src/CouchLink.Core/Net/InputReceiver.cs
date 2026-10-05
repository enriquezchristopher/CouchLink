using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Host side: receives input datagrams and hands valid packets to a callback.</summary>
public sealed class InputReceiver : IDisposable
{
    private readonly UdpClient _udp;

    public InputReceiver(int port) => _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public async Task RunAsync(Action<InputPacket> onPacket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port-unreachable reset on Windows; keep listening
            }

            if (InputPacket.TryParse(result.Buffer, out var packet))
                onPacket(packet);
        }
    }

    public void Dispose() => _udp.Dispose();
}
