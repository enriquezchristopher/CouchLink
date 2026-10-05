using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ControlActionsTests
{
    private static readonly IReadOnlyList<ControlAction> All = ControlActions.All;

    public static TheoryData<PadButtons> EveryButton()
    {
        var data = new TheoryData<PadButtons>();
        foreach (var b in Enum.GetValues<PadButtons>())
            if (b is not PadButtons.None and not PadButtons.Known)
                data.Add(b);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryButton))]
    public void Every_button_is_pressed_by_some_action(PadButtons button)
    {
        Assert.Contains(All, a => a.State.Buttons.HasFlag(button));
    }

    [Fact]
    public void Every_dpad_direction_is_covered()
    {
        var directions = All.Select(a => DpadMath.FromButtons(a.State.Buttons)).ToHashSet();
        foreach (var d in Enum.GetValues<Dpad8>().Where(d => d != Dpad8.None))
            Assert.Contains(d, directions);
    }

    [Theory]
    [InlineData("LX")]
    [InlineData("LY")]
    [InlineData("RX")]
    [InlineData("RY")]
    public void Every_stick_axis_reaches_both_ends(string axis)
    {
        Func<PadState, byte> get = axis switch
        {
            "LX" => s => s.LX,
            "LY" => s => s.LY,
            "RX" => s => s.RX,
            _ => s => s.RY,
        };
        Assert.Contains(All, a => get(a.State) == 0);
        Assert.Contains(All, a => get(a.State) == 255);
    }

    [Fact]
    public void Both_triggers_are_tested_half_and_full()
    {
        Assert.Contains(All, a => a.State.L2 == 128);
        Assert.Contains(All, a => a.State.L2 == 255);
        Assert.Contains(All, a => a.State.R2 == 128);
        Assert.Contains(All, a => a.State.R2 == 255);
    }

    [Fact]
    public void Actions_are_distinct_named_and_never_neutral()
    {
        Assert.Equal(All.Count, All.Select(a => a.State).Distinct().Count());
        Assert.Equal(All.Count, All.Select(a => a.Name).Distinct().Count());
        Assert.DoesNotContain(All, a => a.State == PadState.Neutral);
        Assert.All(All, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public void Each_action_changes_only_one_control()
    {
        foreach (var a in All)
        {
            var s = a.State;
            int changed = 0;
            if ((s.Buttons & ~(PadButtons.L2 | PadButtons.R2)) != PadButtons.None) changed++;
            if (s.LX != 128) changed++;
            if (s.LY != 128) changed++;
            if (s.RX != 128) changed++;
            if (s.RY != 128) changed++;
            if (s.L2 != 0 || s.Buttons.HasFlag(PadButtons.L2)) changed++;
            if (s.R2 != 0 || s.Buttons.HasFlag(PadButtons.R2)) changed++;
            Assert.True(changed == 1, $"{a.Name} changes {changed} controls");
        }
    }
}
