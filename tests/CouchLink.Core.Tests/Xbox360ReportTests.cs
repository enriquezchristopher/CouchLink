using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class Xbox360ReportTests
{
    private static Xbox360Report With(PadButtons buttons) => Xbox360Report.From(PadState.Neutral with { Buttons = buttons });

    [Theory]
    [InlineData(PadButtons.Cross, Xbox360Buttons.A)]
    [InlineData(PadButtons.Circle, Xbox360Buttons.B)]
    [InlineData(PadButtons.Square, Xbox360Buttons.X)]
    [InlineData(PadButtons.Triangle, Xbox360Buttons.Y)]
    [InlineData(PadButtons.L1, Xbox360Buttons.LeftShoulder)]
    [InlineData(PadButtons.R1, Xbox360Buttons.RightShoulder)]
    [InlineData(PadButtons.L3, Xbox360Buttons.LeftThumb)]
    [InlineData(PadButtons.R3, Xbox360Buttons.RightThumb)]
    [InlineData(PadButtons.Options, Xbox360Buttons.Start)]
    [InlineData(PadButtons.Share, Xbox360Buttons.Back)]
    [InlineData(PadButtons.PS, Xbox360Buttons.Guide)]
    [InlineData(PadButtons.DpadUp, Xbox360Buttons.Up)]
    [InlineData(PadButtons.DpadDown, Xbox360Buttons.Down)]
    [InlineData(PadButtons.DpadLeft, Xbox360Buttons.Left)]
    [InlineData(PadButtons.DpadRight, Xbox360Buttons.Right)]
    public void Each_button_maps_to_its_Xbox_button(PadButtons ds4, Xbox360Buttons xbox)
    {
        Assert.Equal(xbox, With(ds4).Buttons);
    }

    [Fact]
    public void The_touchpad_and_the_trigger_bits_press_nothing()
    {
        Assert.Equal(Xbox360Buttons.None, With(PadButtons.Touchpad | PadButtons.L2 | PadButtons.R2).Buttons);
    }

    [Fact]
    public void Left_and_right_together_press_neither()
    {
        Assert.Equal(Xbox360Buttons.None, With(PadButtons.DpadLeft | PadButtons.DpadRight).Buttons);
        Assert.Equal(Xbox360Buttons.Up | Xbox360Buttons.Right, With(PadButtons.DpadUp | PadButtons.DpadRight).Buttons);
        Assert.Equal(Xbox360Buttons.Up, With(PadButtons.DpadUp | PadButtons.DpadLeft | PadButtons.DpadRight).Buttons);
    }

    [Fact]
    public void Triggers_pass_through()
    {
        var r = Xbox360Report.From(PadState.Neutral with { L2 = 200, R2 = 255 });
        Assert.Equal((200, 255), (r.LeftTrigger, r.RightTrigger));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(0, -32768)]
    [InlineData(255, 32767)]
    public void Stick_x_spans_the_full_range_with_center_at_zero(byte ds4, short xbox)
    {
        var r = Xbox360Report.From(PadState.Neutral with { LX = ds4, RX = ds4 });
        Assert.Equal((xbox, xbox), (r.LeftX, r.RightX));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(0, 32767)]      // DS4 up is 0; XInput up is positive
    [InlineData(255, -32768)]
    public void Stick_y_is_flipped(byte ds4, short xbox)
    {
        var r = Xbox360Report.From(PadState.Neutral with { LY = ds4, RY = ds4 });
        Assert.Equal((xbox, xbox), (r.LeftY, r.RightY));
    }

    [Fact]
    public void Neutral_is_all_zero()
    {
        Assert.Equal(new Xbox360Report(Xbox360Buttons.None, 0, 0, 0, 0, 0, 0), Xbox360Report.From(PadState.Neutral));
    }
}
