using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>
/// Host side: receives input on UDP 47803 and drives one virtual DS4 per slot, and streams
/// video to every client it hears from. Until Plan 4 the video is a test pattern.
/// </summary>
internal sealed class HostInputService : IDisposable
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly VideoStreamer _video;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;

    private HostInputService(InputReceiver receiver, ViGEmPadFactory factory)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _video = new VideoStreamer(
            new TestPatternSource(TestPatternSource.SixtyFps), new VideoSender(), Ports.Video, TimeProvider.System, OnVideoError);
        _receiveLoop = _receiver.RunAsync(OnInput, _cts.Token, OnError, (_, _) => _video.RequestKeyframe());
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
    }

    public int PadCount => _pads.Count;

    public VideoSendStats VideoStats => _video.Stats;

    /// <summary>Most recent pad or video error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

    private void OnInput(InputPacket packet, IPAddress from)
    {
        if (_pads.Handle(packet))
            _video.ClientSeen(packet.Slot, from);
    }

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
        AppServices.Log.Write($"Pad error: {e}");
    }

    private void OnVideoError(Exception e)
    {
        LastError = $"Video: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Video error: {e}");
    }

    public static bool TryStart(out HostInputService? service, out string? error)
    {
        service = null;
        // Port first: it's the step most likely to fail, and nothing needs cleaning up yet.
        if (!InputReceiver.TryCreate(Ports.Input, out var receiver, out error))
            return false;
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
        {
            receiver!.Dispose();
            return false;
        }
        service = new HostInputService(receiver!, factory!);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        _video.Dispose();
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
