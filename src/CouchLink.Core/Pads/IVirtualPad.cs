using CouchLink.Core.Input;

namespace CouchLink.Core.Pads;

/// <summary>One virtual controller plugged into the host. Dispose unplugs it.</summary>
public interface IVirtualPad : IDisposable
{
    void Apply(PadState state);
}

public interface IVirtualPadFactory
{
    /// <summary>Plugs in a new virtual pad, already in the Neutral state.</summary>
    IVirtualPad Create();
}
