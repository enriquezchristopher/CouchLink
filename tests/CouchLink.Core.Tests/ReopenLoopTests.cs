using CouchLink.Core.Audio;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class ReopenLoopTests
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(1);

    [Fact]
    public void A_failed_attempt_is_retried_after_the_interval()
    {
        var time = new FakeTimeProvider();
        int attempts = 0;
        using var loop = new ReopenLoop(() => ++attempts >= 3, Retry, time);

        loop.Request(TimeSpan.Zero);
        time.Advance(TimeSpan.Zero);
        Assert.Equal(1, attempts);
        time.Advance(Retry);
        Assert.Equal(2, attempts);
        time.Advance(Retry);
        Assert.Equal(3, attempts); // succeeded
        time.Advance(Retry * 5);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void An_exception_is_reported_and_retried_instead_of_escaping()
    {
        var time = new FakeTimeProvider();
        var errors = new List<Exception>();
        int attempts = 0;
        using var loop = new ReopenLoop(() => ++attempts == 1 ? throw new InvalidOperationException("device gone") : true,
            Retry, time, errors.Add);

        loop.Request(TimeSpan.Zero);
        time.Advance(TimeSpan.Zero);
        time.Advance(Retry);

        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Requests_during_an_attempt_never_run_a_second_one_alongside_it()
    {
        int running = 0, maxRunning = 0, attempts = 0;
        using var release = new ManualResetEventSlim();
        using var loop = new ReopenLoop(() =>
        {
            int now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            if (Interlocked.Increment(ref attempts) == 1)
                release.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref running);
            return true;
        }, Retry, TimeProvider.System);

        loop.Request(TimeSpan.Zero);
        await AudioTestKit.Until(() => Volatile.Read(ref running) == 1);
        loop.Request(TimeSpan.Zero); // e.g. two default-device changes in a row
        loop.Request(TimeSpan.Zero);
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref attempts));

        release.Set();
        await AudioTestKit.Until(() => Volatile.Read(ref attempts) == 2);
        await Task.Delay(100);

        Assert.Equal(2, Volatile.Read(ref attempts)); // the queued requests ran once, after the first
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Dispose_waits_for_a_running_attempt_and_stops_retries()
    {
        int attempts = 0;
        bool finished = false;
        using var started = new ManualResetEventSlim();
        var loop = new ReopenLoop(() =>
        {
            Interlocked.Increment(ref attempts);
            started.Set();
            Thread.Sleep(200);
            finished = true;
            return false; // would retry
        }, TimeSpan.FromMilliseconds(10), TimeProvider.System);

        loop.Request(TimeSpan.Zero);
        started.Wait(TimeSpan.FromSeconds(5));
        loop.Dispose();

        Assert.True(finished);
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref attempts));
    }
}
