using System.Net;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class PadManagerTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.7");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.8");

    private readonly FakePadFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PadManager _manager;

    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 };

    public PadManagerTests() => _manager = new PadManager(_factory, _time);

    private static InputPacket Packet(byte slot, uint seq, PadState? state = null, uint epoch = 1) =>
        new(slot, epoch, seq, state ?? Pressed);

    [Fact]
    public void Plug_creates_a_pad_and_its_input_is_applied()
    {
        Assert.True(_manager.Plug(3, A));
        Assert.True(_manager.IsPlugged(3));
        Assert.True(_manager.Handle(Packet(3, 1), A));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(Pressed, pad.Applied[^1]);
        Assert.Equal(1, _manager.Count);
    }

    [Fact]
    public void Input_for_a_slot_that_is_not_plugged_is_ignored_and_creates_nothing()
    {
        Assert.False(_manager.Handle(Packet(3, 1), A));
        Assert.Empty(_factory.Created);
        Assert.False(_manager.IsPlugged(3));
    }

    [Fact]
    public void Input_from_another_address_is_ignored()
    {
        _manager.Plug(3, A);
        Assert.False(_manager.Handle(Packet(3, 1), B));
        Assert.True(_manager.Handle(Packet(3, 1), A)); // the ignored packet did not use up sequence 1
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slot_outside_2_to_10_cannot_be_plugged(byte slot)
    {
        Assert.False(_manager.Plug(slot, A));
        Assert.Empty(_factory.Created);
    }

    [Fact]
    public void Plugging_a_slot_again_rebinds_it_without_a_new_pad()
    {
        _manager.Plug(2, A);
        _manager.Plug(2, B);
        Assert.Single(_factory.Created);
        Assert.False(_manager.Handle(Packet(2, 1), A));
        Assert.True(_manager.Handle(Packet(2, 1), B));
    }

    [Fact]
    public void Hold_centers_the_pad_keeps_it_plugged_and_ignores_input()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        var pad = _factory.Created[0];

        _manager.Hold(2);

        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
        Assert.False(pad.Disposed);
        Assert.True(_manager.IsPlugged(2));
        Assert.Equal(1, _manager.Count);
        Assert.False(_manager.Handle(Packet(2, 2), A));
    }

    [Fact]
    public void Plug_after_hold_resumes_on_the_same_pad_from_a_new_address()
    {
        _manager.Plug(2, A);
        _manager.Hold(2);
        _manager.Plug(2, B);
        Assert.True(_manager.Handle(Packet(2, 1, epoch: 9), B));
        Assert.Single(_factory.Created);
    }

    [Fact]
    public void Unplug_disposes_the_pad_and_frees_the_slot()
    {
        _manager.Plug(2, A);
        _manager.Unplug(2);
        Assert.True(_factory.Created[0].Disposed);
        Assert.False(_manager.IsPlugged(2));
        Assert.Equal(0, _manager.Count);
        _manager.Unplug(2); // twice is fine
    }

    [Fact]
    public void All_nine_slots_get_separate_pads()
    {
        for (byte slot = 2; slot <= 10; slot++)
            Assert.True(_manager.Plug(slot, A));
        Assert.Equal(9, _factory.Created.Count);
        Assert.Equal(9, _manager.Count);
    }

    [Fact]
    public void Old_or_duplicate_packets_are_not_applied()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 5), A);
        Assert.False(_manager.Handle(Packet(2, 5, PadState.Neutral), A));
        Assert.False(_manager.Handle(Packet(2, 4, PadState.Neutral), A));
        Assert.Equal(Pressed, _factory.Created[0].Applied[^1]);
    }

    [Fact]
    public void Restarted_client_is_accepted_on_same_pad()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 900, epoch: 1), A);
        Assert.True(_manager.Handle(Packet(2, 1, PadState.Neutral, epoch: 2), A));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Fact]
    public void Silent_pad_is_released_after_500ms_exactly_once()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        var pad = _factory.Created[0];

        _time.Advance(TimeSpan.FromMilliseconds(499));
        _manager.ReleaseStale();
        Assert.Equal(Pressed, pad.Applied[^1]);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _manager.ReleaseStale();
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);

        int count = pad.Applied.Count;
        _time.Advance(TimeSpan.FromSeconds(5));
        _manager.ReleaseStale();
        Assert.Equal(count, pad.Applied.Count);
    }

    [Fact]
    public void Silent_pad_is_neutral_within_525ms_when_checked_every_CheckInterval()
    {
        var sincePacket = TimeSpan.Zero;
        void Step(TimeSpan by)
        {
            _time.Advance(by);
            sincePacket += by;
        }

        _manager.Plug(2, A);
        Step(TimeSpan.FromMilliseconds(1));
        _manager.Handle(Packet(2, 1), A);
        sincePacket = TimeSpan.Zero;
        var pad = _factory.Created[0];

        Step(PadManager.CheckInterval - TimeSpan.FromMilliseconds(1));
        _manager.ReleaseStale();
        while (pad.Applied[^1] != PadState.Neutral)
        {
            Step(PadManager.CheckInterval);
            _manager.ReleaseStale();
            Assert.True(sincePacket < TimeSpan.FromSeconds(2), "pad was never released");
        }

        Assert.InRange(sincePacket, PadManager.StaleAfter, TimeSpan.FromMilliseconds(525));
    }

    [Fact]
    public void Active_pad_is_not_released()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        for (uint seq = 2; seq < 100; seq++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(8));
            _manager.Handle(Packet(2, seq), A);
            _manager.ReleaseStale();
        }
        Assert.DoesNotContain(PadState.Neutral, _factory.Created[0].Applied);
    }

    [Fact]
    public void Dispose_unplugs_all_pads()
    {
        _manager.Plug(2, A);
        _manager.Plug(3, B);
        _manager.Dispose();
        Assert.All(_factory.Created, p => Assert.True(p.Disposed));
        Assert.Equal(0, _manager.Count);
    }
}
