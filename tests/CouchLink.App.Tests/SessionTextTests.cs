using CouchLink.App.Presentation;
using CouchLink.Core.Session;

namespace CouchLink.App.Tests;

public class SessionTextTests
{
    [Fact]
    public void Connecting_is_step_one_and_cancels()
    {
        var text = SessionText.For(ClientState.Connecting, "PORTAL-SERVER", 0)!.Value;
        Assert.Equal("Connecting to PORTAL-SERVER…", text.Heading);
        Assert.Equal("", text.Hint);
        Assert.Equal(0, text.Step);
        Assert.Equal("Cancel", text.LeaveText);
        Assert.False(text.Playing);
        Assert.False(text.Reconnecting);
    }

    [Fact]
    public void Waiting_is_step_two_and_says_what_the_host_sees()
    {
        var text = SessionText.For(ClientState.Waiting, "PORTAL-SERVER", 0)!.Value;
        Assert.Equal("Waiting for PORTAL-SERVER to let you in", text.Heading);
        Assert.Equal("The host sees a popup and can allow or deny.", text.Hint);
        Assert.Equal(1, text.Step);
        Assert.Equal("Cancel", text.LeaveText);
    }

    [Fact]
    public void Playing_names_the_player_and_leaves()
    {
        var text = SessionText.For(ClientState.Playing, "PORTAL-SERVER", 3)!.Value;
        Assert.Equal("You're P3", text.Heading);
        Assert.Equal("Playing on PORTAL-SERVER", text.Hint);
        Assert.Equal(2, text.Step);
        Assert.Equal("Leave", text.LeaveText);
        Assert.True(text.Playing);
        Assert.False(text.Reconnecting);
    }

    [Fact]
    public void Reconnecting_keeps_step_three_and_says_the_slot_is_kept()
    {
        var text = SessionText.For(ClientState.Reconnecting, "PORTAL-SERVER", 3)!.Value;
        Assert.Equal("Reconnecting to PORTAL-SERVER…", text.Heading);
        Assert.Equal("Your slot is kept for a minute.", text.Hint);
        Assert.Equal(2, text.Step);
        Assert.Equal("Leave", text.LeaveText);
        Assert.True(text.Reconnecting);
        Assert.False(text.Playing);
    }

    [Fact]
    public void Ended_changes_nothing()
    {
        Assert.Null(SessionText.For(ClientState.Ended, "PORTAL-SERVER", 3));
    }
}
