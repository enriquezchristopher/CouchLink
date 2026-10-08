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
}
