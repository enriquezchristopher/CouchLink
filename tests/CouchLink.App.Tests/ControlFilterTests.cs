using CouchLink.App.Presentation;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

public class ControlFilterTests
{
    [Theory]
    [InlineData(PadControl.LeftUp, "Up")]
    [InlineData(PadControl.RightLeft, "Left")]
    [InlineData(PadControl.DpadDown, "Down")]
    [InlineData(PadControl.DpadRight, "Right")]
    [InlineData(PadControl.Square, "Square")]
    [InlineData(PadControl.L2, "L2")]
    public void Rows_inside_a_stick_or_dpad_group_show_only_the_direction(PadControl control, string name)
    {
        Assert.Equal(name, ControlFilter.RowName(control));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_shows_everything(string? query)
    {
        Assert.True(ControlFilter.Matches(PadControl.Cross, "Buttons", null, query));
    }

    [Fact]
    public void Matches_the_full_control_name_ignoring_case()
    {
        Assert.True(ControlFilter.Matches(PadControl.LeftUp, "Left stick", null, "STICK UP"));
    }

    [Fact]
    public void Matches_the_group_name()
    {
        Assert.True(ControlFilter.Matches(PadControl.L1, "Shoulders", null, "shoulder"));
    }

    [Fact]
    public void Matches_the_label()
    {
        Assert.True(ControlFilter.Matches(PadControl.Square, "Buttons", "Shoot / Steal", "shoot"));
    }

    [Fact]
    public void A_query_that_matches_nothing_hides_the_row()
    {
        Assert.False(ControlFilter.Matches(PadControl.Square, "Buttons", "Shoot", "sprint"));
    }

    [Fact]
    public void Surrounding_spaces_in_the_query_are_ignored()
    {
        Assert.True(ControlFilter.Matches(PadControl.Cross, "Buttons", null, "  cross "));
    }
}
