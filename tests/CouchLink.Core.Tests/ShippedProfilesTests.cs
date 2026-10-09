using CouchLink.Core.Input;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

/// <summary>The profiles that ship in the release zip (src/CouchLink.App/profiles), copied next to the tests.</summary>
public class ShippedProfilesTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "profiles");

    private static ControlProfile Nba2K22()
    {
        var result = ProfileStore.Read(Path.Combine(Folder, "nba-2k22.json"));
        Assert.Null(result.Error);
        return result.Profile!;
    }

    /// <summary>
    /// NBA 2K22's default PC keyboard keys, each on the DualShock 4 button that does the same thing in the
    /// game (Pass is Space on the keyboard and Cross on the pad). Sprint is Enter or Num Enter depending on
    /// the source; Windows gives both the same key code, so Enter covers both.
    /// </summary>
    [Theory]
    [InlineData(PadControl.LeftUp, "W")]
    [InlineData(PadControl.LeftDown, "S")]
    [InlineData(PadControl.LeftLeft, "A")]
    [InlineData(PadControl.LeftRight, "D")]
    [InlineData(PadControl.RightUp, "Num8")]       // pro stick
    [InlineData(PadControl.RightDown, "Num2")]
    [InlineData(PadControl.RightLeft, "Num4")]
    [InlineData(PadControl.RightRight, "Num6")]
    [InlineData(PadControl.Cross, "Space")]        // pass / swap player
    [InlineData(PadControl.Circle, "Num1")]        // bounce pass / take charge
    [InlineData(PadControl.Triangle, "Num3")]      // lob pass / block, rebound
    [InlineData(PadControl.Square, "Num5")]        // shoot / steal
    [InlineData(PadControl.L1, "Tab")]             // call play
    [InlineData(PadControl.R1, "NumAdd")]          // icon pass / icon swap
    [InlineData(PadControl.L2, "LShift")]          // post up / intense-D
    [InlineData(PadControl.R2, "Enter")]           // sprint
    [InlineData(PadControl.Touchpad, "PageUp")]    // timeout / intentional foul
    [InlineData(PadControl.Options, "PageDown")]   // pause
    [InlineData(PadControl.DpadUp, "Up")]          // on-the-fly coaching
    [InlineData(PadControl.DpadDown, "Down")]
    [InlineData(PadControl.DpadLeft, "Left")]
    [InlineData(PadControl.DpadRight, "Right")]
    public void The_NBA_2K22_profile_uses_the_games_keyboard_keys(PadControl control, string key)
    {
        Assert.True(ProfileKeys.TryParse(key, out ushort vk));
        Assert.Equal([vk], Nba2K22().Keys[control]);
    }

    [Fact]
    public void The_NBA_2K22_profile_keeps_CouchLinks_keys_for_buttons_the_game_has_no_key_for()
    {
        var profile = Nba2K22();
        var builtIn = KeyLayout.CreateDefault();
        foreach (var control in new[] { PadControl.L3, PadControl.R3, PadControl.Share })
            Assert.Equal(builtIn.KeysFor(control), profile.Keys[control]);
        Assert.Equal("NBA 2K22", profile.Name);
        Assert.Equal(ControlSettings.DefaultStep, profile.SensitivityStep);
        Assert.False(profile.InvertY);
    }

    [Fact]
    public void The_NBA_2K22_profile_names_the_games_actions()
    {
        var labels = Nba2K22().Labels;
        Assert.Equal("Shoot / Steal", labels[PadControl.Square]);
        Assert.Equal("Pass / Switch player", labels[PadControl.Cross]);
        Assert.Equal("Sprint", labels[PadControl.R2]);
    }

    [Fact]
    public void F1_shows_the_NBA_2K22_labels_with_their_keys()
    {
        var settings = new ControlSettings();
        settings.Apply(Nba2K22());

        var panel = OverlayText.Controls(settings);

        Assert.StartsWith("Controls: NBA 2K22 (F1 hides", panel);
        var lines = panel.Split('\n');
        Assert.Contains(lines, l => l.StartsWith("Shoot / Steal (Square)", StringComparison.Ordinal) && l.EndsWith("Num 5", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Pass / Switch player (Cross)", StringComparison.Ordinal) && l.EndsWith("Space", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Sprint (R2)", StringComparison.Ordinal) && l.EndsWith("Enter", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_shipped_profile_loads()
    {
        var skipped = new List<string>();
        var entries = new ProfileStore(Folder, skipped.Add).List();

        Assert.Empty(skipped);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public void The_NBA_2K22_profile_labels_the_pro_stick_and_F1_lists_it()
    {
        var profile = Nba2K22();
        Assert.Equal("Pro stick up", profile.Labels[PadControl.RightUp]);
        Assert.Equal("Pro stick down", profile.Labels[PadControl.RightDown]);
        Assert.Equal("Pro stick left", profile.Labels[PadControl.RightLeft]);
        Assert.Equal("Pro stick right", profile.Labels[PadControl.RightRight]);

        var settings = new ControlSettings();
        settings.Apply(profile);
        var lines = OverlayText.Controls(settings).Split('\n');
        Assert.Contains(lines, l => l.StartsWith("Pro stick up (Right stick up)", StringComparison.Ordinal) && l.EndsWith("Num 8", StringComparison.Ordinal));
    }
}
