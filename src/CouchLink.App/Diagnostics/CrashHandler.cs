using System.Windows;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App.Diagnostics;

/// <summary>
/// Catches every unhandled exception, writes one crash report, tells the user
/// where it is, and exits. A second crash while the first is being handled
/// waits instead of killing the process under the open dialog.
/// </summary>
internal static class CrashHandler
{
    public const string Heading = "CouchLink crashed.";
    public const string LastTimeHeading = "CouchLink crashed last time.";

    private static int _handling;

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Handle(e.Exception, "UI thread");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), "background thread");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Handle(e.Exception, "unobserved task");
        };
    }

    /// <summary>Shows reports from earlier crashes that the user never saw.</summary>
    public static void ShowPendingReports()
    {
        var pending = AppServices.CrashReports.Unshown();
        if (pending.Count == 0)
            return;
        CrashDialog.ShowAndWait(LastTimeHeading, pending[^1], null);
        foreach (var path in pending)
            AppServices.CrashReports.MarkShown(path);
    }

    private static void Handle(Exception exception, string source)
    {
        if (Interlocked.Exchange(ref _handling, 1) == 1)
        {
            Thread.Sleep(Timeout.Infinite); // first crash owns the report and the exit
            return;
        }

        string? path = null;
        string? saveError = null;
        try
        {
            AppServices.Log.Write($"CRASH ({source}): {exception.GetType().FullName}: {exception.Message}");
            var context = SystemInfo.Collect(SafeMode());
            var report = CrashReportBuilder.Build(context, exception, AppServices.Log.Tail(), Redactor.ForThisMachine());
            path = AppServices.CrashReports.Save(report, DateTime.Now);
        }
        catch (Exception e)
        {
            saveError = e.Message;
        }

        CrashDialog.ShowAndWait(Heading, path, saveError);
        if (path is not null)
            AppServices.CrashReports.MarkShown(path);
        Environment.Exit(1);
    }

    private static string SafeMode()
    {
        try
        {
            return AppServices.DescribeMode();
        }
        catch (Exception e)
        {
            return $"unknown ({e.GetType().Name})";
        }
    }
}
