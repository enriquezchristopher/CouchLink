using CouchLink.Core.Diagnostics;

namespace CouchLink.Core.Tests;

public class CrashReportBuilderTests
{
    private static readonly CrashContext Context = new(
        AppVersion: "0.1.0+abc123",
        Time: new DateTimeOffset(2026, 10, 5, 19, 40, 12, TimeSpan.FromHours(8)),
        Uptime: TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(3),
        Windows: "Microsoft Windows 10.0.19045",
        Runtime: ".NET 10.0.0",
        Cpu: "AMD Ryzen 5 3600 (12 threads)",
        RamBytes: 16L * 1024 * 1024 * 1024,
        Gpu: "AMD Radeon RX 550 (driver 31.0.21001.45002)",
        ViGEmBus: "installed, version 1.22.0.0",
        Mode: "Host (virtual pads: 3)");

    private static readonly Redactor Redactor = new([("PC-07", "<host>"), ("Topher", "<user>")]);

    private static Exception Thrown()
    {
        try
        {
            try
            {
                throw new InvalidOperationException("pad plug-in failed on PC-07");
            }
            catch (Exception inner)
            {
                throw new ApplicationException("host crashed talking to 192.168.1.23", inner);
            }
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static string Build() =>
        CrashReportBuilder.Build(Context, Thrown(), ["19:39:00.000 joined from 192.168.1.40", @"19:40:00.000 log C:\Users\Topher\x"], Redactor);

    [Fact]
    public void Starts_with_title_and_issue_link()
    {
        var lines = Build().Split(Environment.NewLine);
        Assert.Equal("CouchLink crash report", lines[0]);
        Assert.Contains(ProjectLinks.NewIssue, lines[2]);
    }

    [Theory]
    [InlineData("App version: 0.1.0+abc123")]
    [InlineData("Time: 2026-10-05 19:40:12 +08:00")]
    [InlineData("Uptime: 00:12:03")]
    [InlineData("Windows: Microsoft Windows 10.0.19045")]
    [InlineData(".NET: .NET 10.0.0")]
    [InlineData("CPU: AMD Ryzen 5 3600 (12 threads)")]
    [InlineData("RAM: 16.0 GB")]
    [InlineData("GPU: AMD Radeon RX 550 (driver 31.0.21001.45002)")]
    [InlineData("ViGEmBus: installed, version 1.22.0.0")]
    [InlineData("Mode: Host (virtual pads: 3)")]
    public void System_section_lists_every_fact_unredacted(string line)
    {
        Assert.Contains(line + Environment.NewLine, Build());
    }

    [Fact]
    public void Exception_section_has_types_messages_inner_exception_and_stack()
    {
        var report = Build();
        Assert.Contains("System.ApplicationException: host crashed talking to <ip>", report);
        Assert.Contains("System.InvalidOperationException: pad plug-in failed on <host>", report);
        Assert.Contains(nameof(Thrown), report); // stack trace frame
    }

    [Fact]
    public void Log_tail_is_included_and_redacted()
    {
        var report = Build();
        Assert.Contains("Last log lines (2)", report);
        Assert.Contains("joined from <ip>", report);
        Assert.Contains(@"C:\Users\<user>\x", report);
    }

    [Fact]
    public void No_personal_data_survives()
    {
        var report = Build();
        Assert.DoesNotContain("PC-07", report);
        Assert.DoesNotContain("Topher", report);
        Assert.DoesNotContain("192.168.1.", report);
    }

    [Fact]
    public void Empty_log_tail_says_so()
    {
        var report = CrashReportBuilder.Build(Context, Thrown(), [], Redactor);
        Assert.Contains("Last log lines (0)", report);
        Assert.Contains("(no log lines)", report);
    }
}
