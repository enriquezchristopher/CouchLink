using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class DiscoveryLoopbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Announces_reach_a_listener_and_garbage_is_ignored()
    {
        Assert.True(DiscoveryListener.TryCreate(0, out var listener, out _));
        using (listener)
        {
            var heard = Channel.CreateUnbounded<(HostAnnounce Announce, IPAddress From)>();
            using var cts = new CancellationTokenSource();
            var loop = listener!.RunAsync((a, from) => heard.Writer.TryWrite((a, from)), cts.Token);

            using (var raw = new UdpClient())
                raw.Send([1, 2, 3, 4, 5], 5, new IPEndPoint(IPAddress.Loopback, listener.LocalPort));

            using (new DiscoveryBroadcaster(listener.LocalPort, () => HostAnnounce.For(3, 9, "PC-03"), () => [IPAddress.Loopback]))
            {
                using var wait = new CancellationTokenSource(Timeout);
                var (announce, from) = await heard.Reader.ReadAsync(wait.Token);
                Assert.Equal("PC-03", announce.Name);
                Assert.Equal(3, announce.Players);
                Assert.Equal(IPAddress.Loopback, from);
            }

            cts.Cancel();
            await loop.WaitAsync(Timeout);
        }
    }

    [Fact]
    public void A_taken_port_gives_a_message()
    {
        using var taken = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)taken.Client.LocalEndPoint!).Port;

        Assert.False(DiscoveryListener.TryCreate(port, out var listener, out var error));

        Assert.Null(listener);
        Assert.Equal($"Can't search for hosts (port {port} in use).", error);
    }
}
