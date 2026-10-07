namespace CouchLink.Core.Audio;

/// <summary>
/// Reopens a sound device on a timer thread, one attempt at a time. <c>attempt</c> returns true
/// when the device is open; false, or an exception (reported to <c>onError</c>, never let out), tries
/// again after <c>retryInterval</c>. A <see cref="Request"/> while an attempt runs (two
/// default-device changes in a row) queues one more attempt after it, never a second one alongside.
/// <see cref="Dispose"/> stops further attempts and waits for a running one to finish.
/// </summary>
public sealed class ReopenLoop(Func<bool> attempt, TimeSpan retryInterval, TimeProvider time, Action<Exception>? onError = null)
    : IDisposable
{
    private readonly object _gate = new(); // Monitor.Wait needs a plain object
    private ITimer? _timer;
    private bool _running, _pending, _disposed;

    /// <summary>Attempts after <paramref name="delay"/>, or as soon as the running attempt ends.</summary>
    public void Request(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_running)
                _pending = true;
            else
                Schedule(delay);
        }
    }

    /// <summary>Under <c>_gate</c>. An earlier request already scheduled stays; a sooner one replaces it.</summary>
    private void Schedule(TimeSpan delay)
    {
        if (_timer is null)
            _timer = time.CreateTimer(_ => Fire(), null, delay, Timeout.InfiniteTimeSpan);
        else if (delay == TimeSpan.Zero)
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || _running)
                return;
            _running = true;
            _timer?.Dispose();
            _timer = null;
        }

        bool opened = false;
        try
        {
            opened = attempt();
        }
        catch (Exception e)
        {
            onError?.Invoke(e);
        }

        lock (_gate)
        {
            _running = false;
            Monitor.PulseAll(_gate);
            if (_disposed)
                return;
            if (_pending)
            {
                _pending = false;
                Schedule(TimeSpan.Zero);
            }
            else if (!opened)
                Schedule(retryInterval);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            while (_running)
                Monitor.Wait(_gate);
        }
    }
}
