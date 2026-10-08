using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class MouseStickTests
{
    [Fact]
    public void Idle_stick_is_centered()
    {
        Assert.Equal(((byte)128, (byte)128), new MouseStick().Update(0.001));
    }

    [Fact]
    public void Delta_deflects_by_sensitivity()
    {
        var m = new MouseStick();
        m.AddDelta(25, 0); // 25 * 0.02 = 0.5
        Assert.Equal(((byte)192, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void Moving_mouse_down_pushes_stick_down()
    {
        var m = new MouseStick();
        m.AddDelta(0, 25);
        Assert.Equal(((byte)128, (byte)192), m.Update(0.001));
    }

    [Fact]
    public void Returns_to_center_within_50ms_after_mouse_stops()
    {
        var m = new MouseStick();
        m.AddDelta(50, -50);
        (byte X, byte Y) last = default;
        for (int i = 0; i < 50; i++)
            last = m.Update(0.001);
        Assert.Equal(((byte)128, (byte)128), last);
    }

    [Fact]
    public void Huge_flick_clamps_to_full_deflection_without_overflow()
    {
        var m = new MouseStick();
        m.AddDelta(int.MaxValue, 0);
        m.AddDelta(int.MaxValue, 0);
        Assert.Equal(((byte)255, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void Diagonal_flick_clamps_magnitude_to_one()
    {
        var m = new MouseStick();
        m.AddDelta(1000, 1000);
        Assert.Equal(((byte)218, (byte)218), m.Update(0.001));
    }

    [Fact]
    public void Reset_centers_immediately()
    {
        var m = new MouseStick();
        m.AddDelta(40, 40);
        m.Reset();
        Assert.Equal(((byte)128, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void InvertY_flips_vertical_movement()
    {
        var m = new MouseStick { InvertY = true };
        m.AddDelta(0, 25); // mouse toward you: stick up instead of down
        Assert.Equal(((byte)128, (byte)65), m.Update(0.001));
    }
}
