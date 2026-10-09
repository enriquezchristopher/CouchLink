using System.Windows;
using System.Windows.Threading;
using CouchLink.App.Theme;

// Some WPF tests pump the dispatcher while they wait for an animation. With parallel test classes, another
// class's Wpf.Run could run inside that pump and see the other motion mode, so tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CouchLink.App.Tests;

/// <summary>
/// WPF objects need an STA thread, and the theme's pack URIs need an Application. Every WPF test runs
/// on this one thread, which owns one Application for the whole test run.
/// </summary>
internal static class Wpf
{
    private static readonly Lazy<Dispatcher> Ui = new(Start);

    public static void Run(Action action) => Ui.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => Ui.Value.Invoke(func);

    /// <summary>Runs <paramref name="test"/> on the WPF thread with Reduce motion on or off, then turns it off again.</summary>
    public static void WithMotion(bool reduced, Action test) => Run(() =>
    {
        ThemeManager.Install(Application.Current);
        ThemeManager.SetReducedMotion(reduced);
        try
        {
            test();
        }
        finally
        {
            ThemeManager.SetReducedMotion(false);
        }
    });

    /// <summary>On the WPF thread: lets the dispatcher (layout, rendering, animations) run until <paramref name="done"/> or the timeout.</summary>
    public static bool PumpUntil(Func<bool> done, int milliseconds = 3000)
    {
        var stop = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!done())
        {
            if (DateTime.UtcNow > stop)
                return false;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(2);
        }
        return true;
    }

    /// <summary>On the WPF thread: lets the dispatcher run for a while.</summary>
    public static void PumpFor(int milliseconds) => PumpUntil(() => false, milliseconds);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WPF tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
