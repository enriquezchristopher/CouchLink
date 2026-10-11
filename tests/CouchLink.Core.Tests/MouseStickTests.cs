using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class MouseStickTests
{
    /// <summary>
    /// Moves the mouse at a steady speed for <paramref name="ms"/> ticks of 1 ms: <paramref name="counts"/>
    /// counts every <paramref name="everyMs"/> ms, like a mouse polled at 1000 / everyMs Hz.
    /// </summary>
    private static List<(byte X, byte Y)> Move(MouseStick m, int ms, int everyMs, int dx, int dy)
    {
        var outputs = new List<(byte X, byte Y)>();
        for (int t = 0; t < ms; t++)
        {
            if (t % everyMs == 0)
                m.AddDelta(dx, dy);
            outputs.Add(m.Update(0.001));
        }
        return outputs;
    }

    private static List<(byte X, byte Y)> Idle(MouseStick m, int ms) => Move(m, ms, int.MaxValue, 0, 0);

    [Fact]
    public void Idle_stick_is_centered()
    {
        Assert.Equal(((byte)128, (byte)128), new MouseStick().Update(0.001));
    }

    [Fact]
    public void Steady_speed_holds_the_stick_at_sensitivity_past_the_anti_deadzone()
    {
        var m = new MouseStick();
        var outputs = Move(m, 200, 8, 8, 0); // 125 Hz mouse, 1 count/ms
        // 0.2 anti-deadzone + 0.8 * (1 count/ms * 0.4) = 0.52 of full deflection
        Assert.Equal(((byte)194, (byte)128), outputs[^1]);
    }

    [Fact]
    public void A_125_Hz_mouse_moving_steadily_gives_a_steady_stick()
    {
        var m = new MouseStick();
        var xs = Move(m, 200, 8, 8, 0).Skip(100).Select(o => (int)o.X).ToList();
        Assert.True(xs.Max() - xs.Min() <= 2, $"stick swung between {xs.Min()} and {xs.Max()}");
    }

    [Fact]
    public void Slow_movement_from_a_1000_Hz_mouse_is_never_lost()
    {
        // One count every 3 ms: the reports a gaming mouse sends when aiming slowly.
        var m = new MouseStick();
        var xs = Move(m, 200, 3, 1, 0).Skip(20).Select(o => (int)o.X).ToList();
        Assert.True(xs.Min() >= 153, $"stick fell to {xs.Min()} while the mouse was moving");
    }

    [Fact]
    public void A_late_tick_spreads_the_movement_over_the_time_it_covers()
    {
        // The input loop stalls for 100 ms while the mouse keeps moving at 1 count/ms.
        var m = new MouseStick();
        Move(m, 100, 1, 1, 0);
        m.AddDelta(100, 0);
        Assert.Equal(((byte)194, (byte)128), m.Update(0.1)); // still 1 count/ms, not a 100-count flick
    }

    [Fact]
    public void Movement_after_a_short_stall_is_not_counted_twice()
    {
        // A 20 ms stall: its 20 counts must leave the window as newer ones arrive, not linger for 24 ms more.
        var m = new MouseStick();
        Move(m, 100, 1, 1, 0);
        m.AddDelta(20, 0);
        m.Update(0.020);
        var xs = Move(m, 40, 1, 1, 0).Select(o => (int)o.X).ToList();
        Assert.True(xs.Max() <= 196, $"stick rose to {xs.Max()} after the stall");
    }

    [Fact]
    public void The_first_count_already_moves_the_stick_past_the_game_deadzone()
    {
        var m = new MouseStick();
        m.AddDelta(1, 0);
        Assert.True(m.Update(0.001).X >= 153); // 128 + 0.2 * 127
    }

    [Fact]
    public void Faster_movement_deflects_further()
    {
        var slow = Move(new MouseStick(), 100, 1, 1, 0)[^1].X;
        var fast = Move(new MouseStick(), 100, 1, 2, 0)[^1].X;
        Assert.True(fast > slow, $"slow {slow}, fast {fast}");
    }

    [Fact]
    public void Moving_mouse_down_pushes_stick_down()
    {
        var m = new MouseStick();
        var last = Move(m, 50, 1, 0, 1)[^1];
        Assert.Equal((byte)128, last.X);
        Assert.True(last.Y > 128);
    }

    [Fact]
    public void Returns_to_center_within_30ms_after_mouse_stops()
    {
        var m = new MouseStick();
        Move(m, 100, 1, 3, -3);
        Assert.Equal(((byte)128, (byte)128), Idle(m, 30)[^1]);
    }

    [Fact]
    public void A_flick_tilts_the_stick_then_springs_back()
    {
        var m = new MouseStick();
        m.AddDelta(0, -60); // one quick swipe up
        var outputs = Idle(m, 40);
        Assert.True(outputs.Min(o => o.Y) < 64, "the flick never tilted the stick far");
        Assert.Equal(((byte)128, (byte)128), outputs[^1]);
    }

    [Fact]
    public void Huge_flick_clamps_to_full_deflection_without_overflow()
    {
        var m = new MouseStick();
        m.AddDelta(int.MaxValue, 0);
        m.AddDelta(int.MaxValue, 0);
        var outputs = Idle(m, 20);
        Assert.Contains(((byte)255, (byte)128), outputs);
        Assert.All(outputs, o => Assert.Equal((byte)128, o.Y));
    }

    [Fact]
    public void Diagonal_movement_clamps_magnitude_to_one()
    {
        var m = new MouseStick();
        Assert.Equal(((byte)218, (byte)218), Move(m, 100, 1, 1000, 1000)[^1]);
    }

    [Fact]
    public void Reset_centers_immediately()
    {
        var m = new MouseStick();
        Move(m, 50, 1, 2, 2);
        m.Reset();
        Assert.Equal(((byte)128, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void InvertY_flips_vertical_movement()
    {
        var m = new MouseStick { InvertY = true };
        var last = Move(m, 50, 1, 0, 1)[^1]; // mouse toward you: stick up instead of down
        Assert.Equal((byte)128, last.X);
        Assert.True(last.Y < 128);
    }
}
