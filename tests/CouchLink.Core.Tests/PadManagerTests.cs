using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class PadManagerTests
{
    private readonly FakePadFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PadManager _manager;

    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 };

    public PadManagerTests() => _manager = new PadManager(_factory, _time);

    private static InputPacket Packet(byte slot, uint seq, PadState? state = null, uint epoch = 1) =>
        new(slot, epoch, seq, state ?? Pressed);

    [Fact]
    public void First_packet_creates_pad_and_applies_state()
    {
        Assert.True(_manager.Handle(Packet(3, 1)));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(Pressed, pad.Applied[^1]);
        Assert.Equal(1, _manager.Count);
    }

    [Fact]
    public void Same_slot_reuses_its_pad()
    {
        _manager.Handle(Packet(3, 1));
        _manager.Handle(Packet(3, 2, PadState.Neutral));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slot_outside_2_to_10_is_ignored(byte slot)
    {
        Assert.False(_manager.Handle(Packet(slot, 1)));
        Assert.Empty(_factory.Created);
    }

    [Fact]
    public void All_nine_slots_get_separate_pads()
    {
        for (byte slot = 2; slot <= 10; slot++)
            Assert.True(_manager.Handle(Packet(slot, 1)));
        Assert.Equal(9, _factory.Created.Count);
        Assert.Equal(9, _manager.Count);
    }

    [Fact]
    public void Old_or_duplicate_packets_are_not_applied()
    {
        _manager.Handle(Packet(2, 5));
        Assert.False(_manager.Handle(Packet(2, 5, PadState.Neutral)));
        Assert.False(_manager.Handle(Packet(2, 4, PadState.Neutral)));
        Assert.Equal(Pressed, _factory.Created[0].Applied[^1]);
    }

    [Fact]
    public void Restarted_client_is_accepted_on_same_pad()
    {
        _manager.Handle(Packet(2, 900, epoch: 1));
        Assert.True(_manager.Handle(Packet(2, 1, PadState.Neutral, epoch: 2)));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Fact]
    public void Silent_pad_is_released_after_500ms_exactly_once()
    {
        _manager.Handle(Packet(2, 1));
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
        // Checks run at 0, CheckInterval, 2*CheckInterval...; worst case is the last
        // packet landing 1 ms after a check.
        var sincePacket = TimeSpan.Zero;
        void Step(TimeSpan by)
        {
            _time.Advance(by);
            sincePacket += by;
        }

        Step(TimeSpan.FromMilliseconds(1));
        _manager.Handle(Packet(2, 1));
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
        _manager.Handle(Packet(2, 1));
        for (uint seq = 2; seq < 100; seq++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(8));
            _manager.Handle(Packet(2, seq));
            _manager.ReleaseStale();
        }
        Assert.DoesNotContain(PadState.Neutral, _factory.Created[0].Applied);
    }

    [Fact]
    public void Dispose_unplugs_all_pads()
    {
        _manager.Handle(Packet(2, 1));
        _manager.Handle(Packet(3, 1));
        _manager.Dispose();
        Assert.All(_factory.Created, p => Assert.True(p.Disposed));
        Assert.Equal(0, _manager.Count);
    }
}
