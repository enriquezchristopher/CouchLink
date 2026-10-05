using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class UdpInputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Sender_packets_reach_receiver_in_order_and_garbage_is_ignored()
    {
        using var receiver = new InputReceiver(port: 0);
        var received = Channel.CreateUnbounded<InputPacket>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(p => received.Writer.TryWrite(p), cts.Token);

        var host = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using (var raw = new UdpClient())
        {
            raw.Send([1, 2, 3], 3, host);                                 // too short
            raw.Send(new byte[InputPacket.Size], InputPacket.Size, host); // wrong magic
        }

        var pressed = PadState.Neutral with { Buttons = PadButtons.Square, RX = 200 };
        using var sender = new InputSender(host, slot: 4);
        sender.Send(pressed);
        sender.Send(PadState.Neutral);

        using var wait = new CancellationTokenSource(Timeout);
        var first = await received.Reader.ReadAsync(wait.Token);
        var second = await received.Reader.ReadAsync(wait.Token);

        Assert.Equal((byte)4, first.Slot);
        Assert.Equal(pressed, first.State);
        Assert.Equal(PadState.Neutral, second.State);
        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Equal(first.Sequence + 1, second.Sequence);
        Assert.False(received.Reader.TryRead(out _));

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    [Fact]
    public void Each_sender_gets_its_own_epoch()
    {
        var host = new IPEndPoint(IPAddress.Loopback, 9);
        using var a = new InputSender(host, 2);
        using var b = new InputSender(host, 2);
        Assert.NotEqual(a.Epoch, b.Epoch);
    }
}
