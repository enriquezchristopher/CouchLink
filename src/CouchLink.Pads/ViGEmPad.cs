using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace CouchLink.Pads;

/// <summary>A virtual DualShock 4 on ViGEmBus. Each Apply sends one full report.</summary>
public sealed class ViGEmPad : IVirtualPad
{
    private static readonly (PadButtons Flag, DualShock4Button Button)[] Buttons =
    [
        (PadButtons.Cross, DualShock4Button.Cross),
        (PadButtons.Circle, DualShock4Button.Circle),
        (PadButtons.Square, DualShock4Button.Square),
        (PadButtons.Triangle, DualShock4Button.Triangle),
        (PadButtons.L1, DualShock4Button.ShoulderLeft),
        (PadButtons.R1, DualShock4Button.ShoulderRight),
        (PadButtons.L2, DualShock4Button.TriggerLeft),
        (PadButtons.R2, DualShock4Button.TriggerRight),
        (PadButtons.L3, DualShock4Button.ThumbLeft),
        (PadButtons.R3, DualShock4Button.ThumbRight),
        (PadButtons.Options, DualShock4Button.Options),
        (PadButtons.Share, DualShock4Button.Share),
    ];

    private static readonly List<IDualShock4Controller> Removed = [];

    private readonly IDualShock4Controller _pad;

    internal ViGEmPad(ViGEmClient client)
    {
        _pad = client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();
        Apply(PadState.Neutral);
    }

    public void Apply(PadState s)
    {
        foreach (var (flag, button) in Buttons)
            _pad.SetButtonState(button, s.Buttons.HasFlag(flag));
        _pad.SetButtonState(DualShock4SpecialButton.Touchpad, s.Buttons.HasFlag(PadButtons.Touchpad));
        _pad.SetButtonState(DualShock4SpecialButton.Ps, s.Buttons.HasFlag(PadButtons.PS));
        _pad.SetDPadDirection(ToViGEm(DpadMath.FromButtons(s.Buttons)));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, s.LX);
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, s.LY);
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, s.RX);
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, s.RY);
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        _pad.Disconnect();
        // Never free the controller. Connect starts a native notification thread
        // (vigemclient.dll) that nothing can stop; it stays blocked after unplug and
        // reads the target struct if it ever wakes. Freeing the target (Dispose, or
        // the finalizer once unreferenced) is a use-after-free that crashes the host.
        // Rooting it costs about one idle thread and 2 handles per pad ever created.
        lock (Removed)
            Removed.Add(_pad);
    }

    private static DualShock4DPadDirection ToViGEm(Dpad8 d) => d switch
    {
        Dpad8.North => DualShock4DPadDirection.North,
        Dpad8.NorthEast => DualShock4DPadDirection.Northeast,
        Dpad8.East => DualShock4DPadDirection.East,
        Dpad8.SouthEast => DualShock4DPadDirection.Southeast,
        Dpad8.South => DualShock4DPadDirection.South,
        Dpad8.SouthWest => DualShock4DPadDirection.Southwest,
        Dpad8.West => DualShock4DPadDirection.West,
        Dpad8.NorthWest => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };
}
