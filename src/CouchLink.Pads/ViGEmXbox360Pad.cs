using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace CouchLink.Pads;

/// <summary>A virtual Xbox 360 pad on ViGEmBus. Each Apply translates the DS4-shaped state and sends one report.</summary>
public sealed class ViGEmXbox360Pad : IVirtualPad
{
    private readonly IXbox360Controller _pad;

    internal ViGEmXbox360Pad(ViGEmClient client)
    {
        _pad = client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();
        Apply(PadState.Neutral);
    }

    public void Apply(PadState s)
    {
        var r = Xbox360Report.From(s);
        _pad.SetButtonsFull((ushort)r.Buttons);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, r.RightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, r.RightY);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        _pad.Disconnect();
        RemovedTargets.Keep(_pad); // see RemovedTargets: never free a connected target
    }
}
