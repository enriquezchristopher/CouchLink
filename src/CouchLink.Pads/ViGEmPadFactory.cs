using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace CouchLink.Pads;

public sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable
{
    public const string DriverMissingMessage = "ViGEmBus driver not installed";

    private readonly ViGEmClient _client;

    private ViGEmPadFactory(ViGEmClient client, PadKind kind)
    {
        _client = client;
        Kind = kind;
    }

    /// <summary>The pad every Create plugs in.</summary>
    public PadKind Kind { get; }

    public static bool TryCreate(out ViGEmPadFactory? factory, out string? error) =>
        TryCreate(PadKind.DualShock4, out factory, out error);

    public static bool TryCreate(PadKind kind, out ViGEmPadFactory? factory, out string? error)
    {
        try
        {
            factory = new ViGEmPadFactory(new ViGEmClient(), kind);
            error = null;
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            factory = null;
            error = DriverMissingMessage;
            return false;
        }
    }

    public IVirtualPad Create() => Kind == PadKind.Xbox360 ? new ViGEmXbox360Pad(_client) : new ViGEmDs4Pad(_client);

    public void Dispose() => _client.Dispose();
}
