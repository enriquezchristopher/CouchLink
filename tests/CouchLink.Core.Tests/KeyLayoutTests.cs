using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class KeyLayoutTests
{
    private static readonly ushort K = VirtualKeys.Letter('K');
    private static readonly ushort J = VirtualKeys.Letter('J');

    [Fact]
    public void Bind_makes_the_new_key_the_controls_only_key()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, null), layout.Bind(PadControl.Square, VirtualKeys.Space));
        Assert.Equal([VirtualKeys.Space], layout.KeysFor(PadControl.Square));
    }

    [Fact]
    public void Bind_takes_the_key_from_another_control()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, PadControl.Cross), layout.Bind(PadControl.Circle, K));
        Assert.Equal([K], layout.KeysFor(PadControl.Circle));
        Assert.Empty(layout.KeysFor(PadControl.Cross));
    }

    [Fact]
    public void Taking_one_of_two_default_keys_leaves_the_other()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, PadControl.Square), layout.Bind(PadControl.Cross, VirtualKeys.LButton));
        Assert.Equal([J], layout.KeysFor(PadControl.Square));
    }

    [Fact]
    public void Rebinding_a_two_key_default_keeps_only_the_new_key()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.L2, VirtualKeys.LControl);
        Assert.Equal([VirtualKeys.LControl], layout.KeysFor(PadControl.L2));
    }

    [Fact]
    public void Binding_the_key_the_control_already_has_changes_nothing_else()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, null), layout.Bind(PadControl.Cross, K));
        Assert.Equal([K], layout.KeysFor(PadControl.Cross));
    }

    [Theory]
    [InlineData(VirtualKeys.Escape)]
    [InlineData(VirtualKeys.F1)]
    [InlineData(VirtualKeys.F2)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RWin)]
    public void Reserved_keys_are_refused(ushort key)
    {
        var layout = KeyLayout.CreateDefault();
        Assert.True(KeyLayout.IsReserved(key));
        Assert.Equal(BindResult.Reserved, layout.Bind(PadControl.Cross, key));
        Assert.Equal([K], layout.KeysFor(PadControl.Cross));
    }

    [Fact]
    public void Reset_restores_the_defaults()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.Circle, K);
        layout.Bind(PadControl.L2, VirtualKeys.Space);
        layout.ResetToDefault();
        var fresh = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Equal(fresh.KeysFor(control), layout.KeysFor(control));
    }
}
