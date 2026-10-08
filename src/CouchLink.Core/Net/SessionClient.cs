using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Raised on thread-pool threads, never with the client's lock held.</summary>
public interface ISessionClientEvents
{
    void Connected();
    void ConnectFailed();
    void Received(SessionMessage message);
    void Disconnected();
}

/// <summary>
/// Client side: one TCP connection to the host's session port at a time, with a heartbeat every
/// second while connected. Each <see cref="Connect"/> starts a new generation; events from an older
/// connection, or after <see cref="Disconnect"/>, are not raised.
/// </summary>
public sealed class SessionClient : IDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    private readonly IPEndPoint _host;
    private readonly ISessionClientEvents _events;
    private readonly Action<Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly Timer _heartbeat;
    private int _generation;
    private SessionConnection? _connection;
    private CancellationTokenSource? _connecting;
    private bool _disposed;

    public SessionClient(IPEndPoint host, ISessionClientEvents events, Action<Exception>? onError = null)
    {
        _host = host;
        _events = events;
        _onError = onError;
        _heartbeat = new Timer(_ => Send(SessionMessage.Heartbeat), null, HeartbeatInterval, HeartbeatInterval);
    }

    /// <summary>Starts a connection attempt; reports Connected or ConnectFailed.</summary>
    public void Connect()
    {
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
                return;
            CloseCurrent();
            generation = ++_generation;
            _connecting = new CancellationTokenSource();
            token = _connecting.Token;
        }
        _ = Task.Run(() => RunAsync(generation, token));
    }

    public void Send(SessionMessage message)
    {
        lock (_gate)
            _connection?.Send(message);
    }

    /// <summary>Sends what's queued, then closes. Raises nothing.</summary>
    public void Disconnect()
    {
        lock (_gate)
        {
            _generation++;
            CloseCurrent();
        }
    }

    // Cancels only a connect in progress: an established connection closes by flushing, so a Leave
    // queued just before still goes out.
    private void CloseCurrent()
    {
        _connecting?.Cancel();
        _connecting?.Dispose();
        _connecting = null;
        _connection?.CloseAfterSending();
        _connection = null;
    }

    private bool IsCurrent(int generation)
    {
        lock (_gate)
            return generation == _generation && !_disposed;
    }

    private async Task RunAsync(int generation, CancellationToken token)
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(_host, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            tcp.Dispose();
            if (IsCurrent(generation))
                Raise(_events.ConnectFailed);
            return;
        }

        SessionConnection connection;
        lock (_gate)
        {
            if (generation != _generation || _disposed)
            {
                tcp.Dispose();
                return;
            }
            _connecting?.Dispose();
            _connecting = null;
            connection = _connection = new SessionConnection(tcp);
        }
        Raise(_events.Connected);

        try
        {
            while (await SessionFraming.ReadAsync(connection.Stream, CancellationToken.None).ConfigureAwait(false) is { } message)
            {
                if (!IsCurrent(generation))
                    return;
                Raise(() => _events.Received(message));
            }
        }
        catch (InvalidDataException e)
        {
            _onError?.Invoke(e);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // Dropped, or closed by us.
        }

        bool current;
        lock (_gate)
        {
            current = generation == _generation && ReferenceEquals(_connection, connection);
            if (current)
                _connection = null;
        }
        if (current)
        {
            connection.Dispose();
            Raise(_events.Disconnected);
        }
    }

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception e)
        {
            _onError?.Invoke(e);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            CloseCurrent();
        }
        _heartbeat.Dispose();
    }
}
