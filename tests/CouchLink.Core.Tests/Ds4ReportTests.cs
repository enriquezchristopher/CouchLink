using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class Ds4ReportTests
{
    // USB DS4 input report: [0]=0x01 id, [1..4]=LX LY RX RY, [5]=dpad(low 4) + face buttons(high 4),
    // [6]=L1 R1 L2 R2 Share Options L3 R3, [7]=PS, Touchpad (+ counter in high bits), [8]=L2, [9]=R2.
    private static byte[] Report(byte lx = 128, byte ly = 128, byte rx = 128, byte ry = 128,
        byte b5 = 0x08, byte b6 = 0, byte b7 = 0, byte l2 = 0, byte r2 = 0)
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = lx; r[2] = ly; r[3] = rx; r[4] = ry;
        r[5] = b5; r[6] = b6; r[7] = b7; r[8] = l2; r[9] = r2;
        return r;
    }

    [Fact]
    public void Idle_report_decodes_to_neutral()
    {
        Assert.True(Ds4Report.TryDecode(Report(), out var s));
        Assert.Equal(PadState.Neutral, s);
    }

    [Fact]
    public void Axes_and_triggers_are_read()
    {
        Assert.True(Ds4Report.TryDecode(Report(lx: 1, ly: 2, rx: 3, ry: 4, l2: 5, r2: 6), out var s));
        Assert.Equal((1, 2, 3, 4, 5, 6), (s.LX, s.LY, s.RX, s.RY, s.L2, s.R2));
    }

    [Theory]
    [InlineData(0x18, PadButtons.Square)]
    [InlineData(0x28, PadButtons.Cross)]
    [InlineData(0x48, PadButtons.Circle)]
    [InlineData(0x88, PadButtons.Triangle)]
    [InlineData(0x00, PadButtons.DpadUp)]
    [InlineData(0x01, PadButtons.DpadUp | PadButtons.DpadRight)]
    [InlineData(0x02, PadButtons.DpadRight)]
    [InlineData(0x03, PadButtons.DpadDown | PadButtons.DpadRight)]
    [InlineData(0x04, PadButtons.DpadDown)]
    [InlineData(0x05, PadButtons.DpadDown | PadButtons.DpadLeft)]
    [InlineData(0x06, PadButtons.DpadLeft)]
    [InlineData(0x07, PadButtons.DpadUp | PadButtons.DpadLeft)]
    [InlineData(0x08, PadButtons.None)]
    [InlineData(0x0F, PadButtons.None)]
    public void Byte5_decodes_dpad_and_face_buttons(byte b5, PadButtons expected)
    {
        Assert.True(Ds4Report.TryDecode(Report(b5: b5), out var s));
        Assert.Equal(expected, s.Buttons);
    }

    [Theory]
    [InlineData(0x01, PadButtons.L1)]
    [InlineData(0x02, PadButtons.R1)]
    [InlineData(0x04, PadButtons.L2)]
    [InlineData(0x08, PadButtons.R2)]
    [InlineData(0x10, PadButtons.Share)]
    [InlineData(0x20, PadButtons.Options)]
    [InlineData(0x40, PadButtons.L3)]
    [InlineData(0x80, PadButtons.R3)]
    public void Byte6_decodes_shoulder_and_menu_buttons(byte b6, PadButtons expected)
    {
        Assert.True(Ds4Report.TryDecode(Report(b6: b6), out var s));
        Assert.Equal(expected, s.Buttons);
    }

    [Fact]
    public void Byte7_decodes_PS_and_touchpad_and_ignores_counter()
    {
        Assert.True(Ds4Report.TryDecode(Report(b7: 0b1111_1100 | 0x01 | 0x02), out var s));
        Assert.Equal(PadButtons.PS | PadButtons.Touchpad, s.Buttons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Too_short_is_rejected(int length)
    {
        Assert.False(Ds4Report.TryDecode(new byte[length], out _));
    }

    [Fact]
    public void Wrong_report_id_is_rejected()
    {
        var r = Report();
        r[0] = 0x11;
        Assert.False(Ds4Report.TryDecode(r, out _));
    }
}
