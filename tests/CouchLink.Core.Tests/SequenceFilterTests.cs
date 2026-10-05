using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class SequenceFilterTests
{
    [Fact]
    public void First_packet_for_a_slot_is_accepted()
    {
        Assert.True(new SequenceFilter().Accept(2, epoch: 7, sequence: 100));
    }

    [Fact]
    public void Newer_accepted_duplicate_and_older_rejected()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 10));
        Assert.True(f.Accept(2, 7, 11));
        Assert.False(f.Accept(2, 7, 11));
        Assert.False(f.Accept(2, 7, 9));
    }

    [Fact]
    public void Sequence_wraparound_is_treated_as_newer()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, uint.MaxValue));
        Assert.True(f.Accept(2, 7, 0));
        Assert.True(f.Accept(2, 7, 1));
    }

    [Fact]
    public void Restarted_client_with_new_epoch_is_accepted_even_with_low_sequence()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 5000));
        Assert.True(f.Accept(2, 8, 1));
        Assert.False(f.Accept(2, 8, 1));
    }

    [Fact]
    public void Slots_are_independent()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 10));
        Assert.True(f.Accept(3, 9, 1));
        Assert.True(f.Accept(2, 7, 11));
    }
}
