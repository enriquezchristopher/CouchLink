using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class DriftControlTests
{
    /// <summary>Feeds one second (or <paramref name="frames"/>) of the same depth; returns the last answer.</summary>
    private static int Feed(DriftControl drift, int depth, int frames = DriftControl.WindowFrames)
    {
        int last = 0;
        for (int i = 0; i < frames; i++)
            last = drift.Next(depth);
        return last;
    }

    [Fact]
    public void The_first_second_sets_the_baseline_and_small_changes_do_nothing()
    {
        var drift = new DriftControl();

        Assert.Equal(0, Feed(drift, 3));
        Assert.Equal(3, drift.Baseline);
        Assert.Equal(0, Feed(drift, 4)); // 5 ms above: inside the dead band
        Assert.Equal(0, Feed(drift, 2));
        Assert.Equal(0, drift.Corrections);
    }

    [Fact]
    public void Too_full_drops_a_sample_per_frame_until_back_at_the_baseline()
    {
        var drift = new DriftControl();
        Feed(drift, 3);

        Assert.Equal(-1, Feed(drift, 5)); // 10 ms above: dropping from the end of this second
        Assert.Equal(-1, Feed(drift, 4)); // still above the baseline
        Assert.Equal(0, Feed(drift, 3));  // back at it

        Assert.Equal(1 + 200 + 199, drift.Corrections);
    }

    [Fact]
    public void Too_empty_repeats_a_sample_per_frame_until_back_at_the_baseline()
    {
        var drift = new DriftControl();
        Feed(drift, 3);

        Assert.Equal(1, Feed(drift, 1));
        Assert.Equal(0, Feed(drift, 3));
    }

    [Fact]
    public void Reset_learns_the_baseline_again()
    {
        var drift = new DriftControl();
        Feed(drift, 3);
        Assert.Equal(-1, Feed(drift, 6));

        drift.Reset();

        Assert.Null(drift.Baseline);
        Assert.Equal(0, Feed(drift, 2));
        Assert.Equal(2, drift.Baseline);
    }

    [Fact]
    public void A_first_second_that_starts_too_full_is_brought_down_to_20_ms()
    {
        var drift = new DriftControl();

        Feed(drift, 9); // a burst at startup (the device was slow to start) must not become the target

        Assert.Equal(DriftControl.MaxBaselineFrames, drift.Baseline);
        Assert.Equal(-1, Feed(drift, 9));
    }
}
