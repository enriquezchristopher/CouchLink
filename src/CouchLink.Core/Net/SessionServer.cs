using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;

namespace CouchLink.Core.Net;

/// <summary>
/// Host side: the TCP session channel. Every connection's messages, the 250 ms tick and the UI's
/// Allow/Deny/Kick go into one <see cref="HostSession"/> under one lock. An exception anywhere is
/// reported to <c>onError</c> and never ends the process; a malformed frame closes only its
/// connection. Bind with <see cref="TryCreate"/>, then <see cref="Start"/>.
/// </summary>
public sealed class SessionServer : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    private const int TicksPerHeartbeat = 4; // one heartbeat a second

    private readonly TcpListener _listener;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, SessionConnection> _connections = [];
    private readonly CancellationTokenSource _cts = new();
    private HostSession? _session;
    private Action<Exception>? _onError;
    private Task? _acceptLoop;
    private Timer? _timer;
    private int _nextId, _ticks;
    private bool _stopped;

    private SessionServer(TcpListener listener) => _listener = listener;

    /// <summary>Opens the port, or returns a message for the user if it can't be opened.</summary>
    public static bool TryCreate(int port, out SessionServer? server, out string? error)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            listener.Stop();
            server = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"TCP port {port} is already in use. Is CouchLink already hosting on this PC?"
                : $"Could not open TCP port {port}: {e.Message}";
            return false;
        }
        server = new SessionServer(listener);
        error = null;
        return true;
    }

    public int LocalPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts accepting clients. <paramref name="effects"/> run with the server's lock held.</summary>
    public void Start(IHostEffects effects, TimeProvider time, Action<string>? log = null, Action<Exception>? onError = null)
    {
        _onError = onError;
        lock (_gate)
            _session = new HostSession(new Effects(this, effects), time, log);
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _timer = new Timer(_ => OnTimer(), null, TickInterval, TickInterval);
    }

    public bool AllowEveryone
    {
        get { lock (_gate) return _session?.AllowEveryone ?? false; }
        set => Guard(s => s.AllowEveryone = value);
    }

    public IReadOnlyList<PlayerInfo> Players
    {
        get { lock (_gate) return _session?.Players ?? []; }
    }

    public int PlayerCount
    {
        get { lock (_gate) return _session?.PlayerCount ?? 0; }
    }

    public void Allow(int connection) => Guard(s => s.Allow(connection));

    public void Deny(int connection) => Guard(s => s.Deny(connection));

    public void Kick(byte slot) => Guard(s => s.Kick(slot));

    private void Guard(Action<HostSession> action)
    {
        lock (_gate)
        {
            if (_stopped || _session is null)
                return;
            try
            {
                action(_session);
            }
            catch (Exception e)
            {
                _onError?.Invoke(e);
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException
                                      || (e is SocketException && ct.IsCancellationRequested))
            {
                break;
            }
            catch (SocketException e)
            {
                _onError?.Invoke(e);
                continue;
            }

            SessionConnection connection;
            try
            {
                connection = new SessionConnection(tcp);
            }
            catch (Exception e)
            {
                tcp.Dispose();
                _onError?.Invoke(e);
                continue;
            }

            int id;
            lock (_gate)
            {
                if (_stopped)
                {
                    connection.Dispose();
                    break;
                }
                id = ++_nextId;
                _connections[id] = connection;
            }
            Guard(s => s.Connected(id, connection.Address));
            _ = ReadLoopAsync(id, connection);
        }
    }

    private async Task ReadLoopAsync(int id, SessionConnection connection)
    {
        try
        {
            while (await SessionFraming.ReadAsync(connection.Stream, CancellationToken.None).ConfigureAwait(false) is { } message)
                Guard(s => s.Received(id, message));
        }
        catch (InvalidDataException e)
        {
            _onError?.Invoke(new InvalidDataException($"Session connection from {connection.Address}: {e.Message}", e));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // Dropped, or closed by us.
        }

        bool known;
        lock (_gate)
            known = _connections.Remove(id);
        connection.Dispose(); // also after we closed it: the peer has now read everything and closed its end
        if (known)
            Guard(s => s.Disconnected(id));
    }

    private void OnTimer()
    {
        Guard(s => s.Tick());
        if (Interlocked.Increment(ref _ticks) % TicksPerHeartbeat != 0)
            return;
        lock (_gate)
            foreach (var connection in _connections.Values)
                connection.Send(SessionMessage.Heartbeat);
    }

    private void SendTo(int id, SessionMessage message)
    {
        if (_connections.TryGetValue(id, out var connection))
            connection.Send(message);
    }

    private void CloseConnection(int id)
    {
        if (_connections.Remove(id, out var connection))
            connection.CloseAfterSending();
    }

    public void Dispose()
    {
        List<SessionConnection> closing;
        lock (_gate)
        {
            if (_stopped)
                return;
            closing = _connections.Values.ToList();
            try
            {
                _session?.Stop(); // queues HostEnded and closes every connection
            }
            catch (Exception e)
            {
                _onError?.Invoke(e);
            }
            _stopped = true;
        }
        _timer?.Dispose();
        // Each client reads HostEnded, sees the end and closes; forcing the socket shut sooner could lose HostEnded.
        Task.WhenAll(closing.Select(c => c.Closed)).Wait(TimeSpan.FromSeconds(1));
        _cts.Cancel();
        _listener.Stop();
        foreach (var connection in closing)
            connection.Dispose();
        _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
    }

    /// <summary>Called by the session with <see cref="_gate"/> held.</summary>
    private sealed class Effects(SessionServer server, IHostEffects app) : IHostSessionEffects
    {
        public bool PlugPad(byte slot, IPAddress address) => app.PlugPad(slot, address);
        public void HoldPad(byte slot) => app.HoldPad(slot);
        public void UnplugPad(byte slot) => app.UnplugPad(slot);
        public void AddTarget(byte slot, IPAddress address) => app.AddTarget(slot, address);
        public void RemoveTarget(byte slot) => app.RemoveTarget(slot);
        public void AskHost(int connection, string name) => app.AskHost(connection, name);
        public void CloseAsk(int connection) => app.CloseAsk(connection);
        public void PlayersChanged() => app.PlayersChanged();
        public void Send(int connection, SessionMessage message) => server.SendTo(connection, message);
        public void Close(int connection) => server.CloseConnection(connection);
    }
}
