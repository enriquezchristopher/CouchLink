using System.Globalization;
using System.Text;

namespace CouchLink.Core.Diagnostics;

/// <summary>Formats a plain-text crash report for manual upload to a GitHub issue.</summary>
public static class CrashReportBuilder
{
    public static string Build(CrashContext context, Exception exception, IReadOnlyList<string> logTail, Redactor redactor)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append(Environment.NewLine);

        Line("CouchLink crash report");
        Line("======================");
        Line($"Please attach this file to a new issue: {ProjectLinks.NewIssue}");
        Line();
        Line($"App version: {context.AppVersion}");
        Line($"Time: {context.Time.ToString("yyyy-MM-dd HH:mm:ss zzz", inv)}");
        Line($"Uptime: {context.Uptime.ToString(@"hh\:mm\:ss", inv)}");
        Line($"Windows: {context.Windows}");
        Line($".NET: {context.Runtime}");
        Line($"CPU: {context.Cpu}");
        Line($"RAM: {(context.RamBytes / 1024d / 1024 / 1024).ToString("0.0", inv)} GB");
        Line($"GPU: {context.Gpu}");
        Line($"ViGEmBus: {context.ViGEmBus}");
        Line($"Mode: {context.Mode}");
        Line();
        Line("Exception");
        Line("---------");
        Line(redactor.Redact(exception.ToString()));
        Line();
        Line($"Last log lines ({logTail.Count})");
        Line("------------------");
        if (logTail.Count == 0)
            Line("(no log lines)");
        foreach (var line in logTail)
            Line(redactor.Redact(line));
        return sb.ToString();
    }
}
