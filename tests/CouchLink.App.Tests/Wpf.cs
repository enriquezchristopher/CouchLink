using System.Windows;
using System.Windows.Threading;

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
