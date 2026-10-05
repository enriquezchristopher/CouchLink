namespace CouchLink.Core.Input;

/// <summary>Decodes a DualShock 4 USB input report (report id 0x01) back into a PadState.</summary>
public static class Ds4Report
{
    private const byte ReportId = 0x01;
    private const int MinLength = 10;

    private static readonly PadButtons[] DpadByValue =
    [
        PadButtons.DpadUp,
        PadButtons.DpadUp | PadButtons.DpadRight,
        PadButtons.DpadRight,
        PadButtons.DpadDown | PadButtons.DpadRight,
        PadButtons.DpadDown,
        PadButtons.DpadDown | PadButtons.DpadLeft,
        PadButtons.DpadLeft,
        PadButtons.DpadUp | PadButtons.DpadLeft,
    ];

    private static readonly (int Byte, int Bit, PadButtons Button)[] ButtonBits =
    [
        (5, 4, PadButtons.Square),
        (5, 5, PadButtons.Cross),
        (5, 6, PadButtons.Circle),
        (5, 7, PadButtons.Triangle),
        (6, 0, PadButtons.L1),
        (6, 1, PadButtons.R1),
        (6, 2, PadButtons.L2),
        (6, 3, PadButtons.R2),
        (6, 4, PadButtons.Share),
        (6, 5, PadButtons.Options),
        (6, 6, PadButtons.L3),
        (6, 7, PadButtons.R3),
        (7, 0, PadButtons.PS),
        (7, 1, PadButtons.Touchpad),
    ];

    public static bool TryDecode(ReadOnlySpan<byte> report, out PadState state)
    {
        state = default;
        if (report.Length < MinLength || report[0] != ReportId)
            return false;

        int dpad = report[5] & 0x0F;
        var buttons = dpad < DpadByValue.Length ? DpadByValue[dpad] : PadButtons.None;
        foreach (var (index, bit, button) in ButtonBits)
            if ((report[index] & (1 << bit)) != 0)
                buttons |= button;

        state = new PadState(buttons, report[1], report[2], report[3], report[4], report[8], report[9]);
        return true;
    }
}
