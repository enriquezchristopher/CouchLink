using System.Windows;
using System.Windows.Threading;
using CouchLink.App.Diagnostics;
using CouchLink.Core;

namespace CouchLink.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashHandler.Install(this); // first, so even startup crashes are reported
        base.OnStartup(e);
        AppServices.Log.Write($"CouchLink started (args: {string.Join(' ', e.Args)})");
        AppServices.Options = DevOptions.Parse(e.Args);

        var crashTest = e.Args.FirstOrDefault(a => a.StartsWith("--crash-test=", StringComparison.Ordinal))?["--crash-test=".Length..];
        if (crashTest == "startup")
            throw new InvalidOperationException("Crash test: startup");

        try
        {
            CrashHandler.ShowPendingReports();
        }
        catch (Exception ex)
        {
            // A broken report folder must not crash every launch; log it and carry on.
            AppServices.Log.Write($"Could not show pending crash reports: {ex}");
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (crashTest is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => RunCrashTest(crashTest));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Log.Write($"CouchLink exited (code {e.ApplicationExitCode})");
        base.OnExit(e);
    }

    /// <summary>Deliberate crashes for verifying crash reports: --crash-test=ui|background|double|bg-then-ui|startup.</summary>
    private static void RunCrashTest(string kind)
    {
        switch (kind)
        {
            case "ui":
                throw new InvalidOperationException("Crash test: UI thread");
            case "background":
                new Thread(() => throw new InvalidOperationException("Crash test: background thread")).Start();
                break;
            case "bg-then-ui":
                new Thread(() =>
                {
                    Thread.Sleep(200);
                    throw new InvalidOperationException("Crash test: bg then ui (background)");
                }).Start();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    throw new InvalidOperationException("Crash test: bg then ui (UI thread)");
                };
                timer.Start();
                break;
            case "double":
                new Thread(() => throw new InvalidOperationException("Crash test: double A")).Start();
                new Thread(() => throw new InvalidOperationException("Crash test: double B")).Start();
                break;
        }
    }
}
