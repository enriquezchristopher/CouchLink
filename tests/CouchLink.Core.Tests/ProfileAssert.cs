using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

/// <summary>Compares two profiles field by field (records compare dictionaries by reference).</summary>
internal static class ProfileAssert
{
    public static void Same(ControlProfile expected, ControlProfile actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Game, actual.Game);
        Assert.Equal(expected.SensitivityStep, actual.SensitivityStep);
        Assert.Equal(expected.InvertY, actual.InvertY);
        foreach (var control in Enum.GetValues<PadControl>())
        {
            Assert.Equal(KeysOf(expected, control), KeysOf(actual, control));
            Assert.Equal(expected.Labels.GetValueOrDefault(control), actual.Labels.GetValueOrDefault(control));
        }
    }

    private static ushort[] KeysOf(ControlProfile profile, PadControl control) =>
        profile.Keys.TryGetValue(control, out var keys) ? keys.ToArray() : [];
}
