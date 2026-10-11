using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

public class PadPictureTests
{
    private static PadPicture Of(PadState s, PadKind kind = PadKind.DualShock4) => PadPicture.From(s, kind);

    [Theory]
    [InlineData(PadButtons.Cross, PadControl.Cross)]
    [InlineData(PadButtons.Circle, PadControl.Circle)]
    [InlineData(PadButtons.Square, PadControl.Square)]
    [InlineData(PadButtons.Triangle, PadControl.Triangle)]
    [InlineData(PadButtons.L1, PadControl.L1)]
    [InlineData(PadButtons.R1, PadControl.R1)]
    [InlineData(PadButtons.L3, PadControl.L3)]
    [InlineData(PadButtons.R3, PadControl.R3)]
    [InlineData(PadButtons.Options, PadControl.Options)]
    [InlineData(PadButtons.Share, PadControl.Share)]
    [InlineData(PadButtons.Touchpad, PadControl.Touchpad)]
    [InlineData(PadButtons.DpadUp, PadControl.DpadUp)]
    [InlineData(PadButtons.DpadLeft, PadControl.DpadLeft)]
    public void Each_button_lights_its_part(PadButtons button, PadControl part)
    {
        Assert.Equal([part], Of(PadState.Neutral with { Buttons = button }).Lit);
    }

    [Fact]
    public void Neutral_lights_nothing_and_centres_the_sticks()
    {
        var p = Of(PadState.Neutral);
        Assert.Empty(p.Lit);
        Assert.Equal((0.0, 0.0, 0.0, 0.0), (p.LeftX, p.LeftY, p.RightX, p.RightY));
        Assert.Equal(PadPicture.Neutral.Lit, p.Lit);
    }

    [Fact]
    public void Opposite_dpad_directions_light_neither()
    {
        Assert.Empty(Of(PadState.Neutral with { Buttons = PadButtons.DpadLeft | PadButtons.DpadRight }).Lit);
        Assert.Equal([PadControl.DpadUp, PadControl.DpadRight],
            Of(PadState.Neutral with { Buttons = PadButtons.DpadUp | PadButtons.DpadRight }).Lit.Order());
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    [InlineData(255, true)]
    public void A_trigger_lights_above_the_threshold(byte value, bool lit)
    {
        Assert.Equal(lit, Of(PadState.Neutral with { L2 = value }).Lit.Contains(PadControl.L2));
        Assert.Equal(lit, Of(PadState.Neutral with { R2 = value }).Lit.Contains(PadControl.R2));
    }

    [Fact]
    public void A_trigger_bit_lights_it_even_at_zero()
    {
        Assert.Contains(PadControl.L2, Of(PadState.Neutral with { Buttons = PadButtons.L2 }).Lit);
    }

    [Fact]
    public void Full_tilt_reaches_one_each_way()
    {
        var p = Of(PadState.Neutral with { LX = 0, LY = 255, RX = 255, RY = 0 });
        Assert.Equal((-1.0, 1.0, 1.0, -1.0), (p.LeftX, p.LeftY, p.RightX, p.RightY));
    }

    [Fact]
    public void Xbox_never_lights_the_touchpad()
    {
        Assert.Empty(Of(PadState.Neutral with { Buttons = PadButtons.Touchpad }, PadKind.Xbox360).Lit);
    }
}
