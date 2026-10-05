using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>Host side: receives input on UDP 47803 and drives one virtual DS4 per slot.</summary>
internal sealed class HostInputService : IDisposable
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;

    private HostInputService(InputReceiver receiver, ViGEmPadFactory factory)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _receiveLoop = _receiver.RunAsync(p => _pads.Handle(p), _cts.Token, OnError);
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
    }

    public int PadCount => _pads.Count;

    /// <summary>Most recent pad error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

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
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
