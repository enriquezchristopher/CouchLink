using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>The receive loop shared by every CouchLink UDP socket.</summary>
internal static class UdpReceiveLoop
{
    /// <summary>
    /// Receives until cancelled, on thread-pool threads whatever thread started it. An exception
    /// from <paramref name="onDatagram"/> is reported to <paramref name="onError"/> and the loop
    /// keeps going, so one bad datagram never stops the stream.
    /// </summary>
    public static async Task RunAsync(
        UdpClient udp, Action<UdpReceiveResult> onDatagram, CancellationToken ct, Action<Exception>? onError)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct).ConfigureAwait(false); // never wait for the caller's (UI) thread
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port-unreachable reset on Windows; keep listening
            }

            try
            {
                onDatagram(result);
            }
            catch (Exception e)
            {
                onError?.Invoke(e);
            }
        }
    }
}
