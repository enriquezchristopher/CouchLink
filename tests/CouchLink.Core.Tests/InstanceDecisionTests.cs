using CouchLink.Core.Startup;

namespace CouchLink.Core.Tests;

public class InstanceDecisionTests
{
    private static readonly DevOptions Normal = DevOptions.Parse([]);
    private static readonly DevOptions Windowed = DevOptions.Parse(["--windowed-player"]);

    [Fact]
    public void The_first_copy_runs()
    {
        Assert.True(InstanceDecision.ShouldCheck(Normal));
        Assert.Equal(InstanceRole.Run, InstanceDecision.Decide(Normal, ownsMutex: true));
    }

    [Fact]
    public void A_second_copy_hands_off()
    {
        Assert.Equal(InstanceRole.HandOff, InstanceDecision.Decide(Normal, ownsMutex: false));
    }

    [Fact]
    public void A_windowed_player_copy_never_checks()
    {
        Assert.False(InstanceDecision.ShouldCheck(Windowed));
        Assert.Equal(InstanceRole.Bypass, InstanceDecision.Decide(Windowed, ownsMutex: false));
    }
}
