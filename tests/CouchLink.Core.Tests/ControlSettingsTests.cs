using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ControlSettingsTests
{
    [Fact]
    public void Step_5_is_the_original_sensitivity_and_each_step_is_1_3_times()
    {
        Assert.Equal(MouseStick.DefaultSensitivity, ControlSettings.SensitivityFor(5), 6);
        Assert.Equal(0.026, ControlSettings.SensitivityFor(6), 6);
        Assert.Equal(0.0070, ControlSettings.SensitivityFor(1), 4);
        Assert.Equal(0.0743, ControlSettings.SensitivityFor(10), 4);
    }

    [Fact]
    public void Steps_outside_1_to_10_are_clamped()
    {
        var settings = new ControlSettings();
        settings.SetSensitivityStep(0);
        Assert.Equal(1, settings.SensitivityStep);
        settings.SetSensitivityStep(11);
        Assert.Equal(10, settings.SensitivityStep);
    }

    [Fact]
    public void Every_edit_raises_Changed_once()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;

        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(7);
        settings.SetInvertY(true);
        settings.ResetToDefault();

        Assert.Equal(4, changed);
    }

    [Fact]
    public void Refused_or_unchanged_edits_raise_nothing()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;

        settings.Bind(PadControl.Cross, VirtualKeys.F1);
        settings.SetSensitivityStep(ControlSettings.DefaultStep);
        settings.SetInvertY(false);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Reset_restores_layout_sensitivity_and_invert()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(9);
        settings.SetInvertY(true);

        settings.ResetToDefault();

        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Cross));
        Assert.Equal(ControlSettings.DefaultStep, settings.SensitivityStep);
        Assert.False(settings.InvertY);
    }

    private static ControlProfile Profile(string name = "2K22") => new(name, "NBA 2K22", 8, true,
        new Dictionary<PadControl, IReadOnlyList<ushort>>
        {
            [PadControl.Circle] = [VirtualKeys.Letter('K')],
            [PadControl.Square] = [VirtualKeys.Space],
        },
        new Dictionary<PadControl, string> { [PadControl.Square] = "Shoot" });

    [Fact]
    public void Apply_sets_layout_sensitivity_invert_labels_and_name()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());

        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Circle));
        Assert.Empty(settings.Layout.KeysFor(PadControl.Cross)); // left out: no key, not the built-in K
        Assert.Equal(8, settings.SensitivityStep);
        Assert.True(settings.InvertY);
        Assert.Equal("Shoot", settings.LabelFor(PadControl.Square));
        Assert.Null(settings.LabelFor(PadControl.Cross));
        Assert.Equal("2K22", settings.ProfileName);
        Assert.Equal("NBA 2K22", settings.ProfileGame);
        Assert.False(settings.ProfileChanged);
    }

    [Fact]
    public void Apply_raises_Changed_once()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;
        settings.Apply(Profile());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Every_kind_of_edit_after_Apply_marks_the_profile_changed()
    {
        Action<ControlSettings>[] edits =
        [
            s => s.Bind(PadControl.Cross, VirtualKeys.Letter('X')),
            s => s.SetSensitivityStep(2),
            s => s.SetInvertY(false),
            s => s.SetLabel(PadControl.Cross, "Pass"),
        ];
        foreach (var edit in edits)
        {
            var settings = new ControlSettings();
            settings.Apply(Profile());
            edit(settings);
            Assert.True(settings.ProfileChanged);
        }
    }

    [Fact]
    public void Edits_on_the_built_in_layout_mark_nothing()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetLabel(PadControl.Cross, "Pass");
        Assert.Null(settings.ProfileName);
        Assert.False(settings.ProfileChanged);
    }

    [Fact]
    public void Applying_again_clears_the_changed_mark()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());
        settings.SetSensitivityStep(2);
        settings.Apply(Profile("Other"));
        Assert.False(settings.ProfileChanged);
        Assert.Equal("Other", settings.ProfileName);
    }

    [Fact]
    public void Reset_clears_the_profile_and_its_labels()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());
        settings.SetSensitivityStep(2);

        settings.ResetToDefault();

        Assert.Null(settings.ProfileName);
        Assert.Null(settings.ProfileGame);
        Assert.False(settings.ProfileChanged);
        Assert.Null(settings.LabelFor(PadControl.Square));
        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Cross));
    }

    [Theory]
    [InlineData("  Shoot  ", "Shoot")]
    [InlineData("Shoot\r\nfar", "Shoot far")]
    [InlineData("1234567890123456789012345678", "123456789012345678901234")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void SetLabel_trims_caps_and_clears(string? typed, string? stored)
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Old");
        settings.SetLabel(PadControl.Square, typed);
        Assert.Equal(stored, settings.LabelFor(PadControl.Square));
    }

    [Fact]
    public void Setting_the_same_label_raises_nothing()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Shoot");
        int changed = 0;
        settings.Changed += () => changed++;
        settings.SetLabel(PadControl.Square, " Shoot ");
        Assert.Equal(0, changed);
    }

    [Fact]
    public void ToProfile_after_Apply_gives_the_same_profile()
    {
        var settings = new ControlSettings();
        var profile = Profile();
        settings.Apply(profile);
        ProfileAssert.Same(profile, settings.ToProfile("2K22", "NBA 2K22"));
    }

    [Fact]
    public void ToProfile_leaves_out_controls_without_keys_and_tidies_name_and_game()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Circle, VirtualKeys.Letter('K')); // Cross loses its only key
        var profile = settings.ToProfile("  Mine  ", "   ");
        Assert.False(profile.Keys.ContainsKey(PadControl.Cross));
        Assert.Equal("Mine", profile.Name);
        Assert.Null(profile.Game);
        Assert.Null(ProfileFile.Load(ProfileFile.Save(profile)).Error);
    }
}
