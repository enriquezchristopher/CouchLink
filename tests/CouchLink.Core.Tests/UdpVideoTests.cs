using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class UdpVideoTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_keyframe_burst_arrives_complete()
    {
        using var receiver = new VideoReceiver(port: 0);
        Assert.Equal(VideoReceiver.ReceiveBufferBytes, receiver.ReceiveBufferSize);

        var frame = new byte[300_000];
        new Random(1).NextBytes(frame);
        var packets = new FramePacketizer(1).Packetize(1, frame, keyframe: true); // 300 datagrams
        var assembler = new FrameAssembler();
        var done = new TaskCompletionSource<AssembledFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        int received = 0;
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(datagram =>
        {
            Interlocked.Increment(ref received);
            if (assembler.Add(datagram, TimeSpan.Zero) is { } f)
                done.TrySetResult(f);
        }, cts.Token);

        using (var sender = new VideoSender())
            sender.Send(packets, [new IPEndPoint(IPAddress.Loopback, receiver.LocalPort)]);

        Assert.Equal(frame, (await done.Task.WaitAsync(Timeout)).Data);
        var deadline = DateTime.UtcNow + Timeout;
        while (Volatile.Read(ref received) < packets.Count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(packets.Count, Volatile.Read(ref received)); // nothing dropped by the socket buffer

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    [Fact]
    public void TryCreate_reports_a_port_that_is_already_in_use()
    {
        using var taken = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)taken.Client.LocalEndPoint!).Port;

        Assert.False(VideoReceiver.TryCreate(port, out var receiver, out var error));
        Assert.Null(receiver);
        Assert.Equal($"UDP port {port} is already in use. Is CouchLink already joined to a host on this PC?", error);
    }

    [Fact]
    public async Task Keyframe_requests_and_input_both_reach_the_host_with_the_sender_address()
    {
        using var receiver = new InputReceiver(port: 0);
        var inputs = Channel.CreateUnbounded<(InputPacket Packet, IPAddress From)>();
        var requests = Channel.CreateUnbounded<(KeyframeRequest Request, IPAddress From)>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(
            (p, from) => inputs.Writer.TryWrite((p, from)),
            cts.Token,
            onKeyframeRequest: (r, from) => requests.Writer.TryWrite((r, from)));

        using var sender = new InputSender(new IPEndPoint(IPAddress.Loopback, receiver.LocalPort), slot: 6);
        sender.SendKeyframeRequest();
        sender.Send(PadState.Neutral);

        using var wait = new CancellationTokenSource(Timeout);
        var request = await requests.Reader.ReadAsync(wait.Token);
        var input = await inputs.Reader.ReadAsync(wait.Token);
        Assert.Equal((byte)6, request.Request.Slot);
        Assert.Equal(IPAddress.Loopback, request.From);
        Assert.Equal((byte)6, input.Packet.Slot);
        Assert.Equal(IPAddress.Loopback, input.From);

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Timing_pings_reach_the_host_with_the_sender_address()
    {
        using var receiver = new InputReceiver(port: 0);
        var pings = Channel.CreateUnbounded<(TimingPing Ping, IPAddress From)>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync((_, _) => { }, cts.Token,
            onTimingPing: (p, from) => pings.Writer.TryWrite((p, from)));

        using var sender = new InputSender(new IPEndPoint(IPAddress.Loopback, receiver.LocalPort), slot: 4);
        sender.SendTimingPing(clientTicks: 77);

        using var wait = new CancellationTokenSource(Timeout);
        var ping = await pings.Reader.ReadAsync(wait.Token);
        Assert.Equal(new TimingPing(4, 77), ping.Ping);
        Assert.Equal(IPAddress.Loopback, ping.From);

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    /// <summary>Like a busy UI thread: work posted to it never runs.</summary>
    private sealed class BlockedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
        public override void Send(SendOrPostCallback d, object? state) { }
    }

    [Fact]
    public async Task Datagrams_are_handled_off_the_starting_threads_context()
    {
        // The app starts the client on the WPF UI thread; packets must not wait for that thread.
        using var receiver = new VideoReceiver(port: 0);
        using var got = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        var previous = SynchronizationContext.Current;
        Task loop;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new BlockedContext());
            loop = receiver.RunAsync(_ => got.Set(), cts.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using var udp = new UdpClient();
        var packet = new byte[] { 1, 2, 3 };
        udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, receiver.LocalPort));

        Assert.True(got.Wait(Timeout), "the datagram was never handled");
        cts.Cancel();
        await loop.WaitAsync(Timeout); // stops promptly when cancelled
    }
}
