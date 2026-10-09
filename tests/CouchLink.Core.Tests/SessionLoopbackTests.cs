using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class SessionLoopbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class AppEffects : IHostEffects
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Channel<int> Asks { get; } = Channel.CreateUnbounded<int>();

        public bool PlugPad(byte slot, IPAddress address)
        {
            Calls.Enqueue($"plug P{slot}");
            return true;
        }

        public void HoldPad(byte slot) => Calls.Enqueue($"hold P{slot}");
        public void UnplugPad(byte slot) => Calls.Enqueue($"unplug P{slot}");
        public void AddTarget(byte slot, IPAddress address) => Calls.Enqueue($"target+ P{slot}");
        public void RemoveTarget(byte slot) => Calls.Enqueue($"target- P{slot}");
        public void AskHost(int connection, string name) => Asks.Writer.TryWrite(connection);
        public void CloseAsk(int connection) { }
        public void PlayersChanged() { }
    }

    private sealed class ClientEvents(string name) : ISessionClientEvents
    {
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
        private int _heartbeats;

        public SessionClient? Client { get; set; }
        public int Heartbeats => Volatile.Read(ref _heartbeats);
        public bool HasMore => _events.Reader.TryPeek(out _);

        public void Connected()
        {
            _events.Writer.TryWrite("connected");
            Client!.Send(SessionMessage.JoinRequest(name));
        }

        public void ConnectFailed() => _events.Writer.TryWrite("connect failed");

        public void Received(SessionMessage message)
        {
            if (message.Type == SessionMessageType.Heartbeat)
                Interlocked.Increment(ref _heartbeats);
            else
                _events.Writer.TryWrite(message.ToString());
        }

        public void Disconnected() => _events.Writer.TryWrite("disconnected");

        public async Task<string> Next()
        {
            using var cts = new CancellationTokenSource(Timeout);
            return await _events.Reader.ReadAsync(cts.Token);
        }
    }

    private static (SessionServer Server, AppEffects Effects) StartServer(bool allowEveryone, Action<Exception>? onError = null)
    {
        Assert.True(SessionServer.TryCreate(0, out var server, out var error), error);
        var effects = new AppEffects();
        server!.Start(effects, TimeProvider.System, onError: onError);
        server.AllowEveryone = allowEveryone;
        return (server, effects);
    }

    private static (SessionClient Client, ClientEvents Events) Join(SessionServer server, string name = "PC-07")
    {
        var events = new ClientEvents(name);
        var client = new SessionClient(new IPEndPoint(IPAddress.Loopback, server.LocalPort), events);
        events.Client = client;
        client.Connect();
        return (client, events);
    }

    [Fact]
    public async Task A_client_joins_and_is_kicked()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());
                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);

                server.Kick(2);

                Assert.Equal("Kicked", await events.Next());
                Assert.Equal("disconnected", await events.Next());
                Assert.Empty(server.Players);
            }
        }
    }

    [Fact]
    public async Task The_host_lets_a_client_in_with_Allow()
    {
        var (server, effects) = StartServer(allowEveryone: false);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                using var cts = new CancellationTokenSource(Timeout);
                int connection = await effects.Asks.Reader.ReadAsync(cts.Token);

                server.Allow(connection);

                Assert.Equal("Accepted P2", await events.Next());
                Assert.Equal(["plug P2", "target+ P2"], effects.Calls);
            }
        }
    }

    [Fact]
    public async Task A_dropped_connection_reserves_the_slot_and_the_same_name_gets_it_back()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (first, events) = Join(server);
            Assert.Equal("connected", await events.Next());
            Assert.Equal("Accepted P2", await events.Next());

            first.Dispose(); // the client vanishes

            await Until(() => server.Players is [{ State: PlayerState.Reserved }]);
            server.AllowEveryone = false; // a rejoin must not need the popup
            var (second, again) = Join(server);
            using (second)
            {
                Assert.Equal("connected", await again.Next());
                Assert.Equal("Accepted P2", await again.Next());
            }
        }
    }

    [Fact]
    public async Task Heartbeats_flow_both_ways_and_keep_a_quiet_client_in()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());

                await Task.Delay(HostSession.SilenceLimit + TimeSpan.FromSeconds(1));

                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);
                Assert.InRange(events.Heartbeats, 4, 8);
                Assert.False(events.HasMore);
            }
        }
    }

    [Fact]
    public async Task Garbage_closes_only_that_connection()
    {
        var errors = new ConcurrentQueue<Exception>();
        var (server, _) = StartServer(allowEveryone: true, errors.Enqueue);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());

                using var raw = new TcpClient();
                await raw.ConnectAsync(IPAddress.Loopback, server.LocalPort);
                var stream = raw.GetStream();
                await stream.WriteAsync(new byte[] { 0x01, 0x00, 0xAA }); // frame length 1: invalid

                using var cts = new CancellationTokenSource(Timeout);
                var buffer = new byte[64];
                try
                {
                    while (await stream.ReadAsync(buffer, cts.Token) > 0)
                    {
                    }
                }
                catch (IOException)
                {
                    // a reset counts as closed too
                }

                await Until(() => errors.Any(e => e is InvalidDataException));
                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);
                Assert.False(events.HasMore);
            }
        }
    }

    [Fact]
    public async Task Stopping_the_server_tells_the_client()
    {
        var (server, effects) = StartServer(allowEveryone: true);
        var (client, events) = Join(server);
        using (client)
        {
            Assert.Equal("connected", await events.Next());
            Assert.Equal("Accepted P2", await events.Next());

            server.Dispose();

            Assert.Equal("HostEnded", await events.Next());
            Assert.Equal("disconnected", await events.Next());
            Assert.Contains("unplug P2", effects.Calls);
        }
    }

    [Fact]
    public async Task Connecting_to_a_closed_port_fails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var events = new ClientEvents("PC-07");
        using var client = new SessionClient(new IPEndPoint(IPAddress.Loopback, port), events);
        events.Client = client;
        client.Connect();

        Assert.Equal("connect failed", await events.Next());
    }

    [Fact]
    public void A_taken_port_gives_a_message()
    {
        Assert.True(SessionServer.TryCreate(0, out var first, out _));
        using (first)
        {
            int port = first!.LocalPort;
            Assert.False(SessionServer.TryCreate(port, out var second, out var error));
            Assert.Null(second);
            Assert.Equal($"TCP port {port} is already in use. Is CouchLink already hosting on this PC?", error);
        }
    }

    /// <summary>
    /// Closing a socket that still has unread data sends a TCP reset, and a reset can throw away what the peer
    /// hasn't read yet, so the last message has to survive a client that is still sending.
    /// </summary>
    [Fact]
    public async Task Stopping_the_server_tells_a_client_that_is_still_sending()
    {
        for (int run = 0; run < 20; run++)
        {
            var (server, _) = StartServer(allowEveryone: true);
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());
                for (int i = 0; i < 20; i++)
                    client.Send(SessionMessage.Heartbeat);

                server.Dispose();

                Assert.Equal("HostEnded", await events.Next());
                Assert.Equal("disconnected", await events.Next());
            }
        }
    }

    [Fact]
    public async Task A_kicked_client_that_is_still_sending_is_told_why()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            for (int run = 0; run < 20; run++)
            {
                var (client, events) = Join(server);
                using (client)
                {
                    Assert.Equal("connected", await events.Next());
                    Assert.Equal("Accepted P2", await events.Next());
                    for (int i = 0; i < 20; i++)
                        client.Send(SessionMessage.Heartbeat);

                    server.Kick(2);

                    Assert.Equal("Kicked", await events.Next());
                    Assert.Equal("disconnected", await events.Next());
                }
            }
        }
    }
}
