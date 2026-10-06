using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Host side: receives input datagrams and hands valid packets to a callback.</summary>
public sealed class InputReceiver : IDisposable
{
    private readonly UdpClient _udp;

    public InputReceiver(int port) => _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));

    /// <summary>Opens the port, or returns a message for the user if it can't be opened.</summary>
    public static bool TryCreate(int port, out InputReceiver? receiver, out string? error)
    {
        try
        {
            receiver = new InputReceiver(port);
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            receiver = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"UDP port {port} is already in use. Is CouchLink already hosting on this PC?"
                : $"Could not open UDP port {port}: {e.Message}";
            return false;
        }
    }

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    /// <summary>
    /// Receives until cancelled. An exception from <paramref name="onPacket"/> (e.g. a
    /// virtual pad failing to plug in) is reported to <paramref name="onError"/> and the
    /// loop keeps going, so one bad packet never stops input for every player.
    /// </summary>
    public async Task RunAsync(Action<InputPacket> onPacket, CancellationToken ct, Action<Exception>? onError = null)
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

            if (!InputPacket.TryParse(result.Buffer, out var packet))
                continue;
            try
            {
                onPacket(packet);
            }
            catch (Exception e)
            {
                onError?.Invoke(e);
            }
        }
    }

    public void Dispose() => _udp.Dispose();
}
