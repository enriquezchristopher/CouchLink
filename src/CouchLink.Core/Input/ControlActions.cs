namespace CouchLink.Core.Input;

public sealed record ControlAction(string Name, PadState State);

/// <summary>Every single DS4 action, one control at a time, for exhaustive pad checks.</summary>
public static class ControlActions
{
    private static readonly PadState N = PadState.Neutral;

    public static IReadOnlyList<ControlAction> All { get; } =
    [
        Button("Cross", PadButtons.Cross),
        Button("Circle", PadButtons.Circle),
        Button("Square", PadButtons.Square),
        Button("Triangle", PadButtons.Triangle),
        Button("L1", PadButtons.L1),
        Button("R1", PadButtons.R1),
        Button("L3", PadButtons.L3),
        Button("R3", PadButtons.R3),
        Button("Options", PadButtons.Options),
        Button("Share", PadButtons.Share),
        Button("PS", PadButtons.PS),
        Button("Touchpad", PadButtons.Touchpad),
        Button("D-pad Up", PadButtons.DpadUp),
        Button("D-pad Down", PadButtons.DpadDown),
        Button("D-pad Left", PadButtons.DpadLeft),
        Button("D-pad Right", PadButtons.DpadRight),
        Button("D-pad Up-Left", PadButtons.DpadUp | PadButtons.DpadLeft),
        Button("D-pad Up-Right", PadButtons.DpadUp | PadButtons.DpadRight),
        Button("D-pad Down-Left", PadButtons.DpadDown | PadButtons.DpadLeft),
        Button("D-pad Down-Right", PadButtons.DpadDown | PadButtons.DpadRight),
        new("Left stick full left", N with { LX = 0 }),
        new("Left stick full right", N with { LX = 255 }),
        new("Left stick full up", N with { LY = 0 }),
        new("Left stick full down", N with { LY = 255 }),
        new("Right stick full left", N with { RX = 0 }),
        new("Right stick full right", N with { RX = 255 }),
        new("Right stick full up", N with { RY = 0 }),
        new("Right stick full down", N with { RY = 255 }),
        new("L2 half", N with { Buttons = PadButtons.L2, L2 = 128 }),
        new("L2 full", N with { Buttons = PadButtons.L2, L2 = 255 }),
        new("R2 half", N with { Buttons = PadButtons.R2, R2 = 128 }),
        new("R2 full", N with { Buttons = PadButtons.R2, R2 = 255 }),
    ];

    private static ControlAction Button(string name, PadButtons buttons) => new(name, N with { Buttons = buttons });
}
