using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// One TCP session connection. Messages are queued and written by one writer task, so sending never
/// blocks the session's lock. <see cref="CloseAfterSending"/> flushes the queue, ends the sending side and
/// waits for the peer to close, giving up after <see cref="CloseGrace"/>. Closing outright while the peer's
/// messages are still unread would send a TCP reset, which can throw away the last message (HostEnded,
/// Kicked) before the peer reads it.
/// </summary>
internal sealed class SessionConnection : IDisposable
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);

    private readonly TcpClient _tcp;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SessionConnection(TcpClient tcp)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        Stream = tcp.GetStream();
        Address = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
        Writer = WriteLoopAsync();
    }

    public NetworkStream Stream { get; }
    public IPAddress Address { get; }

    /// <summary>Completes once everything queued is written and the sending side is ended (or writing failed).</summary>
    public Task Writer { get; }

    /// <summary>Completes once the socket is closed: the peer closed its end, or <see cref="Dispose"/> ran.</summary>
    public Task Closed => _closed.Task;

    public void Send(SessionMessage message) => _outgoing.Writer.TryWrite(SessionFraming.Encode(message));

    public void CloseAfterSending()
    {
        if (_outgoing.Writer.TryComplete())
            _ = Task.Delay(CloseGrace).ContinueWith(_ => _tcp.Dispose(), TaskScheduler.Default);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var bytes in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
                await Stream.WriteAsync(bytes).ConfigureAwait(false);
            // The peer reads everything sent, then sees the end and closes; the read loop then disposes.
            _tcp.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // The peer is gone; the read side reports it.
            Dispose();
        }
    }

    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        _tcp.Dispose();
        _closed.TrySetResult();
    }
}
