using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace CouchLink.Pads;

public sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable
{
    public const string DriverMissingMessage = "ViGEmBus driver not installed";

    private readonly ViGEmClient _client;

    private ViGEmPadFactory(ViGEmClient client) => _client = client;

    public static bool TryCreate(out ViGEmPadFactory? factory, out string? error)
    {
        try
        {
            factory = new ViGEmPadFactory(new ViGEmClient());
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

    public IVirtualPad Create() => new ViGEmPad(_client);

    public void Dispose() => _client.Dispose();
}
