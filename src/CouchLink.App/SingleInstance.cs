using System.IO;
using CouchLink.App.Input;
using CouchLink.Core;
using CouchLink.Core.Startup;

namespace CouchLink.App;

/// <summary>
/// One CouchLink per Windows sign-in. A second launch tells the first to come forward and exits;
/// with --windowed-player it is skipped so one-PC testing can run two copies. A crashed copy's
/// mutex is released by Windows, so it never blocks the next start.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CouchLink.SingleInstance";
    private const string EventName = @"Local\CouchLink.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <summary>False: another copy was told to come forward, so exit. <paramref name="instance"/> is null when not checked.</summary>
    public static bool TryClaim(DevOptions options, out SingleInstance? instance)
    {
        instance = null;
        if (!InstanceDecision.ShouldCheck(options))
            return true;
        Mutex mutex;
        EventWaitHandle activate;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            if (InstanceDecision.Decide(options, created) == InstanceRole.Run)
            {
                instance = new SingleInstance(mutex, activate);
                return true;
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            AppServices.Log.Write($"Single-instance check failed ({e.Message}); starting anyway");
            return true;
        }

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY); // a fresh launch may hand over focus
        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return false;
    }

    /// <summary>Runs <paramref name="activate"/> on a pool thread whenever a later launch signals.</summary>
    public void OnActivate(Action activate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        {
            try
            {
                activate();
            }
            catch (Exception e)
            {
                AppServices.Log.Write($"Bringing CouchLink forward failed: {e}");
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>On the thread that claimed it (the UI thread).</summary>
    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
