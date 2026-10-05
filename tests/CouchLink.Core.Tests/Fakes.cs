using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

internal sealed class FakePad : IVirtualPad
{
    public List<PadState> Applied { get; } = [];
    public bool Disposed { get; private set; }
    public void Apply(PadState state) => Applied.Add(state);
    public void Dispose() => Disposed = true;
}

internal sealed class FakePadFactory : IVirtualPadFactory
{
    public List<FakePad> Created { get; } = [];

    public IVirtualPad Create()
    {
        var pad = new FakePad();
        Created.Add(pad);
        return pad;
    }
}
