using System.Net;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>Sends one frame's shard packets to every client.</summary>
public interface IVideoPacketSender : IDisposable
{
    void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets);
}

/// <summary>
/// Host side: one unicast copy of every packet per client. Packets go out packet by packet
/// across clients, so every client gets the frame at about the same time.
/// </summary>
public sealed class VideoSender : IVideoPacketSender
{
    public const int SendBufferBytes = 4 * 1024 * 1024;

    private readonly UdpClient _udp = new(AddressFamily.InterNetwork);

    public VideoSender() => _udp.Client.SendBufferSize = SendBufferBytes;

    public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
    {
        foreach (var packet in packets)
            foreach (var target in targets)
            {
                try
                {
                    _udp.Send(packet, packet.Length, target);
                }
                catch (SocketException)
                {
                    // That client is unreachable right now; it drops out of the targets once its input stops.
                }
            }
    }

    public void Dispose() => _udp.Dispose();
}
