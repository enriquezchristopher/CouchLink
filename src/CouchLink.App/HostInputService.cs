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

    private HostInputService(ViGEmPadFactory factory)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = new InputReceiver(Ports.Input);
        _receiveLoop = _receiver.RunAsync(p => _pads.Handle(p), _cts.Token);
        _staleTimer = new Timer(_ => _pads.ReleaseStale(), null, 100, 100);
    }

    public int PadCount => _pads.Count;

    public static bool TryStart(out HostInputService? service, out string? error)
    {
        service = null;
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
            return false;
        service = new HostInputService(factory!);
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
