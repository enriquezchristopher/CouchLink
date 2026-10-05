using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

public class IsolationCheckTests
{
    [Fact]
    public void Signatures_are_all_different_and_not_neutral()
    {
        var sigs = Enumerable.Range(0, 9).Select(IsolationCheck.SignatureFor).ToList();
        Assert.Equal(9, sigs.Distinct().Count());
        Assert.DoesNotContain(PadState.Neutral, sigs);
    }

    [Fact]
    public void FindExactlyOne_returns_the_single_matching_device()
    {
        var target = IsolationCheck.SignatureFor(0);
        PadState?[] read = [PadState.Neutral, target, null];
        Assert.Equal(1, IsolationCheck.FindExactlyOne(target, read));
    }

    [Fact]
    public void FindExactlyOne_is_null_when_no_device_matches()
    {
        PadState?[] read = [PadState.Neutral, null];
        Assert.Null(IsolationCheck.FindExactlyOne(IsolationCheck.SignatureFor(0), read));
    }

    [Fact]
    public void FindExactlyOne_is_null_when_input_leaked_to_two_devices()
    {
        var target = IsolationCheck.SignatureFor(0);
        PadState?[] read = [target, target];
        Assert.Null(IsolationCheck.FindExactlyOne(target, read));
    }

    [Fact]
    public void AllDistinct_passes_only_for_a_full_one_to_one_mapping()
    {
        Assert.True(IsolationCheck.AllDistinct([2, 0, 1]));
        Assert.False(IsolationCheck.AllDistinct([2, 2, 1]));
        Assert.False(IsolationCheck.AllDistinct([2, null, 1]));
        Assert.False(IsolationCheck.AllDistinct([]));
    }
}
