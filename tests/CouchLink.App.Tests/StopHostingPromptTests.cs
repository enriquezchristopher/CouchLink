using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class StopHostingPromptTests
{
    [Fact]
    public void No_players_needs_no_prompt() => Assert.Null(StopHostingPrompt.For([]));

    [Fact]
    public void One_player_is_named() =>
        Assert.Equal("PC-07 will be disconnected.", StopHostingPrompt.For(["PC-07"]));

    [Fact]
    public void Two_players_are_named() =>
        Assert.Equal("PC-07 and PC-11 will be disconnected.", StopHostingPrompt.For(["PC-07", "PC-11"]));

    [Fact]
    public void Three_players_are_named() =>
        Assert.Equal("PC-07, PC-11 and PC-12 will be disconnected.", StopHostingPrompt.For(["PC-07", "PC-11", "PC-12"]));

    [Fact]
    public void Four_or_more_are_counted() =>
        Assert.Equal("4 players will be disconnected.", StopHostingPrompt.For(["A", "B", "C", "D"]));
}
