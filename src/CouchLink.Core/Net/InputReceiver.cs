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
    /// Receives until cancelled, passing each valid input packet, keyframe request and timing ping on with
    /// the sender's address. An exception from a callback (e.g. a virtual pad failing to plug
    /// in) is reported to <paramref name="onError"/> and the loop keeps going, so one bad
    /// packet never stops input for every player.
    /// </summary>
    public Task RunAsync(
        Action<InputPacket, IPAddress> onPacket,
        CancellationToken ct,
        Action<Exception>? onError = null,
        Action<KeyframeRequest, IPAddress>? onKeyframeRequest = null,
        Action<TimingPing, IPAddress>? onTimingPing = null) =>
        UdpReceiveLoop.RunAsync(_udp, result =>
        {
            var from = result.RemoteEndPoint.Address;
            if (InputPacket.TryParse(result.Buffer, out var packet))
                onPacket(packet, from);
            else if (onKeyframeRequest is not null && KeyframeRequest.TryParse(result.Buffer, out var request))
                onKeyframeRequest(request, from);
            else if (onTimingPing is not null && TimingPing.TryParse(result.Buffer, out var ping))
                onTimingPing(ping, from);
        }, ct, onError);

    public void Dispose() => _udp.Dispose();
}
