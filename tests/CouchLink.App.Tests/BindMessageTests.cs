using CouchLink.App.Presentation;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

public class BindMessageTests
{
    private const ushort Num5 = 0x65, Esc = 0x1B;

    [Fact]
    public void Reserved_names_the_key() =>
        Assert.Equal("Esc is reserved. Press another key.", BindMessage.Reserved(Esc));

    [Fact]
    public void Nothing_moved_says_nothing() => Assert.Null(BindMessage.Moved(Num5, null, 0));

    [Fact]
    public void A_control_left_without_keys_is_called_out() =>
        Assert.Equal("Num 5 moved here from Circle. Circle has no key now.", BindMessage.Moved(Num5, PadControl.Circle, 0));

    [Fact]
    public void A_control_with_another_key_left_is_not_called_out() =>
        Assert.Equal("Num 5 moved here from Circle.", BindMessage.Moved(Num5, PadControl.Circle, 1));

    [Fact]
    public void Stick_controls_use_their_full_name() =>
        Assert.Equal("Num 5 moved here from D-pad up. D-pad up has no key now.", BindMessage.Moved(Num5, PadControl.DpadUp, 0));
}
