using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

/// <summary>
/// Raw Input events recorded from a real keyboard with Num Lock on, Left Shift held while Num8 and Num5 were
/// tapped. Replayed through the same translation the app uses, into the mapper.
/// </summary>
public class RawKeyboardReplayTests
{
    private const ushort Down = 0x00;
    private const ushort Up = 0x01; // RI_KEY_BREAK

    private static readonly (ushort VKey, ushort MakeCode, ushort Flags)[] ShiftThenNum8ThenNum5 =
    [
        (0x10, 0x2A, Down), // Left Shift pressed
        (0x10, 0x2A, Down), // typematic repeat
        (0x10, 0x48, Down), // Windows' Shift event before Num8
        (0x26, 0x48, Down), // Num8, reported as the Up arrow because Shift is held
        (0x26, 0x48, Up),
        (0x10, 0x48, Up), // Windows' Shift event after Num8
        (0x10, 0x4C, Down),
        (0x0C, 0x4C, Down), // Num5, reported as Clear
        (0x0C, 0x4C, Up),
        (0x10, 0x4C, Up),
    ];

    private static void Apply(InputMapper mapper, (ushort VKey, ushort MakeCode, ushort Flags) e)
    {
        var vk = VirtualKeys.FromRawEvent(e.VKey, e.MakeCode, e.Flags, numLockOn: true);
        if (vk == 0)
            return;
        if ((e.Flags & Up) != 0)
            mapper.KeyUp(vk);
        else
            mapper.KeyDown(vk);
    }

    [Fact]
    public void Shift_stays_held_while_numpad_keys_are_tapped()
    {
        var mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        foreach (var e in ShiftThenNum8ThenNum5)
        {
            Apply(mapper, e);
            Assert.Equal(PadButtons.R2, mapper.Tick(0.001).Buttons); // Left Shift is R2, held the whole time
        }
    }

    [Fact]
    public void Shift_is_released_when_the_real_Shift_key_goes_up()
    {
        var mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        foreach (var e in ShiftThenNum8ThenNum5)
            Apply(mapper, e);

        Apply(mapper, (0x10, 0x2A, Up));

        Assert.Equal(PadButtons.None, mapper.Tick(0.001).Buttons);
    }

    [Fact]
    public void The_numpad_keys_arrive_as_themselves()
    {
        var keys = ShiftThenNum8ThenNum5
            .Select(e => VirtualKeys.FromRawEvent(e.VKey, e.MakeCode, e.Flags, numLockOn: true))
            .Where(vk => vk != 0)
            .Distinct()
            .ToArray();

        Assert.Equal(new ushort[] { VirtualKeys.LShift, 0x68, 0x65 }, keys); // Shift, Num8, Num5
    }
}
