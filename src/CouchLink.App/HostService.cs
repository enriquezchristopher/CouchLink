using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>What the host lobby shows. Called from network threads: post to the UI thread.</summary>
internal interface IHostUi
{
    void PlayersChanged();
    void AskHost(int connection, string name);
    void CloseAsk(int connection);
}

/// <summary>
/// Host side. The session server on TCP 47801 decides who is in; this plugs their pads (bound to
/// their address), applies their input from UDP 47803, streams video (the screen, or --test-pattern)
/// and sound (loopback, or --test-tone) to them, and announces the host on UDP 47800. Video can be
/// restarted with new settings while clients stay in.
/// </summary>
internal sealed class HostService : IDisposable, IHostEffects
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly SessionServer _server;
    private readonly DiscoveryBroadcaster _broadcaster;
    private readonly HostAudio _audio;
    private readonly IHostUi _ui;
    private readonly Lock _media = new();
    private readonly Dictionary<byte, IPAddress> _targets = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;
    private HostVideo _video;

    private HostService(InputReceiver receiver, SessionServer server, ViGEmPadFactory factory, StreamSettings settings, IHostUi ui)
    {
        _ui = ui;
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _server = server;
        _video = HostVideo.Start(settings, OnVideoError);
        _audio = HostAudio.Start(OnAudioError);
        _receiveLoop = _receiver.RunAsync((packet, from) => _pads.Handle(packet, from), _cts.Token, OnError,
            onKeyframeRequest: (_, _) => { lock (_media) _video.RequestKeyframe(); },
            onTimingPing: (ping, from) => { lock (_media) _video.ReplyToTimingPing(ping, from); });
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
        _server.Start(this, TimeProvider.System, message => AppServices.Log.Write(message), OnError);
        _broadcaster = new DiscoveryBroadcaster(Ports.Discovery,
            () => HostAnnounce.For(_server.PlayerCount, HostSession.Capacity, PcName.ThisPc), onError: OnError);
    }

    public static bool TryStart(StreamSettings settings, IHostUi ui, out HostService? service, out string? error)
    {
        service = null;
        // Ports first: they are the steps most likely to fail, and nothing needs cleaning up yet.
        if (!InputReceiver.TryCreate(Ports.Input, out var receiver, out error))
            return false;
        if (!SessionServer.TryCreate(Ports.Session, out var server, out error))
        {
            receiver!.Dispose();
            return false;
        }
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
        {
            server!.Dispose();
            receiver!.Dispose();
            return false;
        }
        service = new HostService(receiver!, server!, factory!, settings, ui);
        return true;
    }

    public IReadOnlyList<PlayerInfo> Players => _server.Players;

    public bool AllowEveryone
    {
        set => _server.AllowEveryone = value;
    }

    public int PadCount => _pads.Count;

    /// <summary>Most recent pad, session, video or audio error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

    public void Allow(int connection) => _server.Allow(connection);

    public void Deny(int connection) => _server.Deny(connection);

    public void Kick(byte slot) => _server.Kick(slot);

    /// <summary>Restarts video with new settings; clients stay in and get a keyframe. UI thread.</summary>
    public void ChangeSettings(StreamSettings settings)
    {
        lock (_media)
        {
            _video.Dispose();
            _video = HostVideo.Start(settings, OnVideoError);
            foreach (var (slot, address) in _targets)
                _video.AddTarget(slot, address); // forces a keyframe
        }
        AppServices.Log.Write($"Stream settings changed: {StreamSettings.Label(settings.Resolution)} at {settings.FrameRate} fps");
    }

    public string DescribeStreams()
    {
        lock (_media)
            return $"{_video.Describe()}\n{_audio.Describe()}";
    }

    // IHostEffects: called by the session with its lock held.

    public bool PlugPad(byte slot, IPAddress address)
    {
        try
        {
            return _pads.Plug(slot, address);
        }
        catch (Exception e)
        {
            OnError(e); // e.g. ViGEmBus refused another pad; the client is told
            return false;
        }
    }

    public void HoldPad(byte slot) => _pads.Hold(slot);

    public void UnplugPad(byte slot)
    {
        try
        {
            _pads.Unplug(slot);
        }
        catch (Exception e)
        {
            OnError(e);
        }
    }

    public void AddTarget(byte slot, IPAddress address)
    {
        lock (_media)
        {
            _targets[slot] = address;
            _video.AddTarget(slot, address);
            _audio.AddTarget(slot, address);
        }
    }

    public void RemoveTarget(byte slot)
    {
        lock (_media)
        {
            _targets.Remove(slot);
            _video.RemoveTarget(slot);
            _audio.RemoveTarget(slot);
        }
    }

    public void AskHost(int connection, string name) => _ui.AskHost(connection, name);

    public void CloseAsk(int connection) => _ui.CloseAsk(connection);

    public void PlayersChanged() => _ui.PlayersChanged();

    private void ReleaseStale()
    {
        try
        {
            _pads.ReleaseStale();
        }
        catch (Exception e)
        {
            OnError(e); // an unhandled exception here would kill the host process
        }
    }

    private void OnError(Exception e)
    {
        LastError = $"{e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Host error: {e}");
    }

    private void OnVideoError(Exception e)
    {
        LastError = $"Video: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Video error: {e}");
    }

    private void OnAudioError(Exception e)
    {
        LastError = $"Audio: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Audio error: {e}");
    }

    public void Dispose()
    {
        _broadcaster.Dispose(); // stop advertising first
        _server.Dispose();      // tells every client, unplugs every pad through the session
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        lock (_media)
            _video.Dispose();
        _audio.Dispose();
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
