using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class DpadMathTests
{
    [Theory]
    [InlineData(PadButtons.None, Dpad8.None)]
    [InlineData(PadButtons.DpadUp, Dpad8.North)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadRight, Dpad8.NorthEast)]
    [InlineData(PadButtons.DpadRight, Dpad8.East)]
    [InlineData(PadButtons.DpadDown | PadButtons.DpadRight, Dpad8.SouthEast)]
    [InlineData(PadButtons.DpadDown, Dpad8.South)]
    [InlineData(PadButtons.DpadDown | PadButtons.DpadLeft, Dpad8.SouthWest)]
    [InlineData(PadButtons.DpadLeft, Dpad8.West)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadLeft, Dpad8.NorthWest)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadDown, Dpad8.None)]
    [InlineData(PadButtons.DpadLeft | PadButtons.DpadRight | PadButtons.DpadUp, Dpad8.North)]
    [InlineData(PadButtons.Cross, Dpad8.None)]
    public void FromButtons_maps_dpad_flags_to_direction(PadButtons buttons, Dpad8 expected)
    {
        Assert.Equal(expected, DpadMath.FromButtons(buttons));
    }

    [Fact]
    public void Neutral_is_centered_with_nothing_pressed()
    {
        var n = PadState.Neutral;
        Assert.Equal(PadButtons.None, n.Buttons);
        Assert.Equal((128, 128, 128, 128, 0, 0), (n.LX, n.LY, n.RX, n.RY, n.L2, n.R2));
    }
}
