using System.Net;
using System.Windows.Threading;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>What the main window does as the session moves on. Always called on the UI thread.</summary>
internal interface IClientUi
{
    void StartPlaying(byte slot);
    void Ended(string? message);
    void StateChanged();
}

/// <summary>
/// Client side: one join to one host. Runs <see cref="ClientSession"/> over a <see cref="SessionClient"/>
/// under one lock, ticks it every 250 ms, and hands what the UI must do to the UI thread.
/// </summary>
internal sealed class ClientSessionService : IDisposable, IClientSessionEffects, ISessionClientEvents
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly IClientUi _ui;
    private readonly SessionClient _client;
    private readonly ClientSession _session;
    private readonly Timer _tick;

    public ClientSessionService(IPAddress host, string hostName, Dispatcher dispatcher, IClientUi ui)
    {
        Host = host;
        HostName = hostName;
        _dispatcher = dispatcher;
        _ui = ui;
        _client = new SessionClient(new IPEndPoint(host, Ports.Session), this, e => AppServices.Log.Write($"Session error: {e}"));
        _session = new ClientSession(this, TimeProvider.System, hostName, PcName.ThisPc);
        AppServices.Log.Write($"Joining {hostName} ({host})");
        Guard(s => s.Start());
        _tick = new Timer(_ => Guard(s => s.Tick()), null, TickInterval, TickInterval);
    }

    public IPAddress Host { get; }
    public string HostName { get; }

    public ClientState State
    {
        get { lock (_gate) return _session.State; }
    }

    public byte Slot
    {
        get { lock (_gate) return _session.Slot; }
    }

    /// <summary>The player's status line: "Reconnecting..." while the host is lost, otherwise null. Any thread.</summary>
    public string? PlayerStatus => State == ClientState.Reconnecting ? OverlayText.Reconnecting : null;

    public void Leave() => Guard(s => s.Leave());

    public void Fail(string message) => Guard(s => s.Fail(message));

    private void Guard(Action<ClientSession> action)
    {
        lock (_gate)
        {
            try
            {
                action(_session);
            }
            catch (Exception e)
            {
                AppServices.Log.Write($"Session error: {e}"); // never let a timer or socket thread take the app down
            }
        }
    }

    // ISessionClientEvents: thread-pool threads.
    void ISessionClientEvents.Connected() => Guard(s => s.Connected());
    void ISessionClientEvents.ConnectFailed() => Guard(s => s.ConnectFailed());
    void ISessionClientEvents.Received(SessionMessage message) => Guard(s => s.Received(message));
    void ISessionClientEvents.Disconnected() => Guard(s => s.Disconnected());

    // IClientSessionEffects: called with _gate held.
    void IClientSessionEffects.Connect() => _client.Connect();
    void IClientSessionEffects.Send(SessionMessage message) => _client.Send(message);
    void IClientSessionEffects.Disconnect() => _client.Disconnect();

    void IClientSessionEffects.StartPlaying(byte slot)
    {
        AppServices.Log.Write($"Accepted by {HostName} as P{slot}");
        _dispatcher.InvokeAsync(() => _ui.StartPlaying(slot));
    }

    void IClientSessionEffects.Ended(string? message)
    {
        AppServices.Log.Write($"Session with {HostName} ended: {message ?? "left"}");
        _dispatcher.InvokeAsync(() => _ui.Ended(message));
    }

    void IClientSessionEffects.StateChanged() => _dispatcher.InvokeAsync(_ui.StateChanged);

    public void Dispose()
    {
        _tick.Dispose();
        _client.Dispose();
    }
}
