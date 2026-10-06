using System.IO;
using CouchLink.Core;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App;

/// <summary>App-wide services shared by the crash handler and the windows.</summary>
internal static class AppServices
{
    public static DateTime Started { get; } = DateTime.Now;

    public static FileLog Log { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchLink", "Logs"));

    public static CrashReportStore CrashReports { get; } = CrashReportStore.Default();

    /// <summary>Describes what the app is doing, for crash reports (e.g. "Host (virtual pads: 3)").</summary>
    public static Func<string> DescribeMode { get; set; } = () => "Idle";

    /// <summary>Command-line developer switches, read at startup.</summary>
    public static DevOptions Options { get; set; } = new(false, null);
}
