using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>
/// Host side: receives input on UDP 47803 and drives one virtual DS4 per slot, and streams
/// video (the host screen, or --test-pattern) and sound (loopback, or --test-tone) to every client it hears from.
/// </summary>
internal sealed class HostInputService : IDisposable
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly HostVideo _video;
    private readonly HostAudio _audio;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;

    private HostInputService(InputReceiver receiver, ViGEmPadFactory factory, StreamSettings settings)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _video = HostVideo.Start(settings, OnVideoError);
        _audio = HostAudio.Start(OnAudioError);
        _receiveLoop = _receiver.RunAsync(OnInput, _cts.Token, OnError,
            onKeyframeRequest: (_, _) => _video.RequestKeyframe(),
            onTimingPing: _video.ReplyToTimingPing);
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
    }

    public int PadCount => _pads.Count;

    public string DescribeStreams() => $"{_video.Describe()}\n{_audio.Describe()}";

    /// <summary>Most recent pad or video error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

    private void OnInput(InputPacket packet, IPAddress from)
    {
        if (_pads.Handle(packet))
        {
            _video.ClientSeen(packet.Slot, from);
            _audio.ClientSeen(packet.Slot, from);
        }
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

    private void OnAudioError(Exception e)
    {
        LastError = $"Audio: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Audio error: {e}");
    }

    public static bool TryStart(StreamSettings settings, out HostInputService? service, out string? error)
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
        service = new HostInputService(receiver!, factory!, settings);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        _video.Dispose();
        _audio.Dispose();
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
