using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// One TCP session connection. Messages are queued and written by one writer task, so sending never
/// blocks the session's lock. <see cref="CloseAfterSending"/> flushes the queue and then closes,
/// giving up after <see cref="CloseGrace"/> if the peer has stopped reading.
/// </summary>
internal sealed class SessionConnection : IDisposable
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);

    private readonly TcpClient _tcp;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

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

    /// <summary>Completes once everything queued is written (or writing failed) and the socket is closed.</summary>
    public Task Writer { get; }

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
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // The peer is gone; the read side reports it.
        }
        finally
        {
            _tcp.Dispose();
        }
    }

    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        _tcp.Dispose();
    }
}
