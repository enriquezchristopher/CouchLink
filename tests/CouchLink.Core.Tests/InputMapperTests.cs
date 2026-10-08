using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class InputMapperTests
{
    private static InputMapper NewMapper() => new(KeyLayout.CreateDefault(), new MouseStick());

    private static readonly ushort W = VirtualKeys.Letter('W');
    private static readonly ushort D = VirtualKeys.Letter('D');
    private static readonly ushort S = VirtualKeys.Letter('S');
    private static readonly ushort J = VirtualKeys.Letter('J');
    private static readonly ushort K = VirtualKeys.Letter('K');

    [Fact]
    public void Nothing_held_is_neutral()
    {
        Assert.Equal(PadState.Neutral, NewMapper().Tick(0.001));
    }

    [Fact]
    public void W_pushes_left_stick_up()
    {
        var m = NewMapper();
        m.KeyDown(W);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)128, (byte)1), (s.LX, s.LY));
    }

    [Fact]
    public void W_and_D_give_normalized_diagonal()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(D);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)218, (byte)38), (s.LX, s.LY));
    }

    [Fact]
    public void W_and_S_cancel()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(S);
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Theory]
    [InlineData((ushort)'K', PadButtons.Cross)]
    [InlineData((ushort)'J', PadButtons.Square)]
    [InlineData(VirtualKeys.LButton, PadButtons.Square)]
    [InlineData((ushort)'L', PadButtons.Circle)]
    [InlineData((ushort)'I', PadButtons.Triangle)]
    [InlineData((ushort)'Q', PadButtons.L1)]
    [InlineData((ushort)'E', PadButtons.R1)]
    [InlineData((ushort)'F', PadButtons.L3)]
    [InlineData(VirtualKeys.MButton, PadButtons.R3)]
    [InlineData(VirtualKeys.Return, PadButtons.Options)]
    [InlineData(VirtualKeys.Back, PadButtons.Share)]
    [InlineData(VirtualKeys.Tab, PadButtons.Touchpad)]
    [InlineData(VirtualKeys.Up, PadButtons.DpadUp)]
    [InlineData(VirtualKeys.Down, PadButtons.DpadDown)]
    [InlineData(VirtualKeys.Left, PadButtons.DpadLeft)]
    [InlineData(VirtualKeys.Right, PadButtons.DpadRight)]
    public void Default_layout_maps_key_to_button(ushort vk, PadButtons expected)
    {
        var m = NewMapper();
        m.KeyDown(vk);
        Assert.Equal(expected, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Ctrl_and_Shift_are_full_L2_and_R2()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.LControl);
        m.KeyDown(VirtualKeys.LShift);
        var s = m.Tick(0.001);
        Assert.Equal(PadButtons.L2 | PadButtons.R2, s.Buttons);
        Assert.Equal(((byte)255, (byte)255), (s.L2, s.R2));
    }

    [Fact]
    public void Right_Ctrl_and_right_Shift_are_L2_and_R2_too()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.RControl);
        m.KeyDown(VirtualKeys.RShift);
        Assert.Equal(PadButtons.L2 | PadButtons.R2, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Releasing_one_of_two_held_Shift_keys_keeps_R2_pressed()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.LShift);
        m.KeyDown(VirtualKeys.RShift);
        m.KeyUp(VirtualKeys.LShift);
        Assert.Equal(PadButtons.R2, m.Tick(0.001).Buttons);
        m.KeyUp(VirtualKeys.RShift);
        Assert.Equal(PadButtons.None, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Releasing_one_of_two_held_Ctrl_keys_keeps_L2_pressed()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.RControl);
        m.KeyDown(VirtualKeys.LControl);
        m.KeyUp(VirtualKeys.RControl);
        Assert.Equal(PadButtons.L2, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Control_stays_down_while_any_bound_key_is_held()
    {
        var m = NewMapper();
        m.KeyDown(J);
        m.KeyDown(VirtualKeys.LButton);
        m.KeyUp(J);
        Assert.Equal(PadButtons.Square, m.Tick(0.001).Buttons);
        m.KeyUp(VirtualKeys.LButton);
        Assert.Equal(PadButtons.None, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Mouse_moves_right_stick()
    {
        var m = NewMapper();
        m.MouseMove(25, 0);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)192, (byte)128), (s.RX, s.RY));
    }

    [Fact]
    public void Unbound_key_does_nothing()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.Letter('Z'));
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Fact]
    public void SyncHeld_drops_keys_whose_key_up_was_lost()
    {
        // e.g. Ctrl+Alt+Del / Win+L: Ctrl's key-down arrived, its key-up never did
        var m = NewMapper();
        m.KeyDown(VirtualKeys.LControl);
        m.KeyDown(W);
        m.SyncHeld(vk => vk == W); // only W is still physically down
        var s = m.Tick(0.001);
        Assert.Equal(PadButtons.None, s.Buttons);
        Assert.Equal((byte)0, s.L2);
        Assert.Equal((byte)1, s.LY);
    }

    [Fact]
    public void ReleaseAll_clears_held_keys_and_mouse_stick()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(K);
        m.KeyDown(VirtualKeys.LShift);
        m.MouseMove(40, 40);
        m.ReleaseAll();
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Fact]
    public void An_edit_counts_from_the_next_tick()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        m.KeyDown(VirtualKeys.Space);
        Assert.Equal(PadButtons.Cross, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void An_edit_releases_held_keys()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        m.KeyDown(K);
        m.MouseMove(40, 0);
        settings.SetInvertY(true);
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Fact]
    public void A_control_without_keys_is_never_pressed()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        settings.Bind(PadControl.Circle, K); // Cross has no key now
        m.KeyDown(K);
        Assert.Equal(PadButtons.Circle, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Sensitivity_and_invert_come_from_the_settings()
    {
        var settings = new ControlSettings();
        settings.SetSensitivityStep(10);
        settings.SetInvertY(true);
        using var m = new InputMapper(settings);
        m.MouseMove(0, 10); // 10 counts * 0.0743 = 0.743 of full deflection, upward
        Assert.Equal((byte)34, m.Tick(0.001).RY);
    }

    [Fact]
    public void After_Dispose_edits_no_longer_reach_the_mapper()
    {
        var settings = new ControlSettings();
        var m = new InputMapper(settings);
        m.Dispose();
        m.KeyDown(K);
        settings.SetInvertY(true); // would release K if still subscribed
        Assert.Equal(PadButtons.Cross, m.Tick(0.001).Buttons);
    }

    [Fact]
    public async Task A_tick_reads_one_layout_for_every_control()
    {
        var layout = KeyLayout.CreateDefault();
        var onCross = new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [K] };
        var onCircle = new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Circle] = [K] };
        layout.Replace(onCross);
        var mapper = new InputMapper(layout, new MouseStick());
        mapper.KeyDown(K);

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var swapper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                layout.Replace(onCircle);
                layout.Replace(onCross);
            }
        });
        int ticks = 0;
        while (!stop.IsCancellationRequested)
        {
            var buttons = mapper.Tick(0.001).Buttons & (PadButtons.Cross | PadButtons.Circle);
            Assert.True(buttons is PadButtons.Cross or PadButtons.Circle, $"tick {ticks}: {buttons}");
            ticks++;
        }
        await swapper;
    }

    [Fact]
    public void A_profile_loaded_while_a_key_is_held_presses_nothing()
    {
        var settings = new ControlSettings();
        using var mapper = new InputMapper(settings);
        mapper.KeyDown(K);
        Assert.Equal(PadButtons.Cross, mapper.Tick(0.001).Buttons);

        settings.Apply(new ControlProfile("P", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Circle] = [K] },
            new Dictionary<PadControl, string>()));

        Assert.Equal(PadState.Neutral, mapper.Tick(0.001)); // released, not Circle until pressed again
        mapper.KeyDown(K);
        Assert.Equal(PadButtons.Circle, mapper.Tick(0.001).Buttons);
    }
}
