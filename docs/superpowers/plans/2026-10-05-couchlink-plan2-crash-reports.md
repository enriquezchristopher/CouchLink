# CouchLink Plan 2: Crash Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When CouchLink crashes, save a privacy-safe plain-text crash report to a known folder and tell the user exactly where it is, so they can attach it to a GitHub issue.

**Architecture:** Four pure, unit-tested pieces in `CouchLink.Core/Diagnostics`: `Redactor` (strips PC name, user name, IPs), `FileLog` (rolling log + in-memory tail of the last 200 lines), `CrashReportBuilder` (formats the report), `CrashReportStore` (saves to `%LOCALAPPDATA%\CouchLink\CrashReports`, falls back to `%TEMP%`, keeps 20, tracks which were shown). `CouchLink.App` wires them to every unhandled-exception hook and shows a crash dialog on its own UI thread, plus a "crashed last time" dialog on the next start.

**Tech Stack:** C# / .NET 10, WPF, xUnit, Microsoft.Extensions.TimeProvider.Testing, Windows UI Automation (verification only).

**Spec:** `docs/superpowers/specs/2026-10-05-couchlink-design.md` (section 7 Diagnostics, section 7.1 Crash reports)

**Builds on:** Plan 1 (`plan1-pads-input`, PR #1). Branch: `plan2-crash-reports`.

## Global Constraints

- C#, .NET 10; Windows projects `net10.0-windows`, `x64`; `TreatWarningsAsErrors`.
- No Claude attribution trailers in commit messages.
- Report folder: **`%LOCALAPPDATA%\CouchLink\CrashReports\`**; fallback **`%TEMP%\CouchLink\CrashReports\`**.
- Report file name: **`couchlink-crash-YYYYMMDD-HHMMSS.txt`** (local time), plain text, UTF-8.
- Keep the newest **20** reports per folder.
- Log file: **`%LOCALAPPDATA%\CouchLink\Logs\couchlink.log`**, rolling, **5 files** total; report includes the **last 200** log lines.
- Report contents: app version, time, uptime, Windows, .NET, CPU, RAM, GPU + driver, mode (Host/Client/Idle, slot, pad count), ViGEmBus installed + version, full exception (type, message, stack, all inner exceptions), log tail.
- **No personal data** in exception text or log lines: PC name -> `<host>`, Windows user name -> `<user>`, IPv4/IPv6 -> `<ip>`.
- Dialog text: **"CouchLink crashed. A report was saved to:"** + full path; buttons **Open folder**, **Copy path**, **Report on GitHub** (`https://github.com/enriquezchristopher/CouchLink/issues/new`), **Close**; line **"Please attach this file to a new issue so we can fix it."**
- Next start shows the same dialog for any report not yet shown, headed **"CouchLink crashed last time."**
- Writing or showing a report must never itself crash or hang the app.

## Review Focus

1. **Two threads crash at nearly the same time** -> exactly one report and one dialog; the second crash must not kill the process while the first dialog is open. Pinned in Task 5 (guard + manual `--crash-test double` check).
2. **Report folder not writable** (permissions, disk full, a file where the folder should be) -> report goes to the `%TEMP%` fallback and the dialog shows that path; if both fail the dialog shows the error instead of a path. Pinned in Task 4 (fallback + both-fail tests) and Task 5 (null path dialog).
3. **Personal data in exception messages, stack-trace paths, or log lines** (PC name, `C:\Users\<name>\...`, LAN IPs) -> replaced before the file is written, while version numbers like `1.22.0.0` in the system section stay readable. Pinned in Task 1 and Task 3.
4. **Crash during startup, before the main window exists** -> still produces a report and dialog. Pinned in Task 5 (handlers installed first in `OnStartup`, `--crash-test startup` check).
5. **Logging fails** (log folder unwritable) -> the app keeps running and the in-memory tail still feeds the crash report. Pinned in Task 2.

---

## File Structure

```
src/CouchLink.Core/Diagnostics/
  Redactor.cs              PC name / user name / IP scrubbing
  FileLog.cs               rolling file log + in-memory tail
  CrashContext.cs          system + app facts for a report
  ProjectLinks.cs          GitHub issue URL
  CrashReportBuilder.cs    formats the report text
  CrashReportStore.cs      save, fallback, prune, shown-tracking
src/CouchLink.App/
  App.xaml                 (modify) drop StartupUri, startup in code
  App.xaml.cs              (modify) install crash handling, pending dialog, --crash-test
  AppServices.cs           shared log, store, mode description
  Diagnostics/SystemInfo.cs    collects CrashContext on Windows
  Diagnostics/CrashHandler.cs  hooks all unhandled-exception events
  Diagnostics/CrashDialog.cs   the dialog (code-only WPF window)
  MainWindow.xaml(.cs)     (modify) Crash reports button, mode description, logging
  HostInputService.cs      (modify) log pad errors
tests/CouchLink.Core.Tests/
  RedactorTests.cs
  FileLogTests.cs
  CrashReportBuilderTests.cs
  CrashReportStoreTests.cs
```

---

### Task 1: Redactor

**Files:**
- Create: `src/CouchLink.Core/Diagnostics/Redactor.cs`
- Test: `tests/CouchLink.Core.Tests/RedactorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class Redactor(IEnumerable<(string Value, string Placeholder)> names)` with `string Redact(string text)` and `static Redactor ForThisMachine()` (machine name -> `<host>`, user name -> `<user>`).

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/RedactorTests.cs`:
```csharp
using CouchLink.Core.Diagnostics;

namespace CouchLink.Core.Tests;

public class RedactorTests
{
    private static readonly Redactor R = new([("PC-07", "<host>"), ("Topher", "<user>")]);

    [Fact]
    public void Machine_name_is_replaced_case_insensitively()
    {
        Assert.Equal("connect to <host> failed", R.Redact("connect to pc-07 failed"));
    }

    [Fact]
    public void User_name_in_paths_is_replaced()
    {
        Assert.Equal(@"at C:\Users\<user>\AppData\x.cs:line 4", R.Redact(@"at C:\Users\Topher\AppData\x.cs:line 4"));
    }

    [Theory]
    [InlineData("from 192.168.1.23:47803", "from <ip>:47803")]
    [InlineData("IP 10.0.0.5.", "IP <ip>.")]
    [InlineData("hosts 10.0.0.1 and 10.0.0.2", "hosts <ip> and <ip>")]
    [InlineData("fe80::1c2b:3d4e%12 down", "<ip>%12 down")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", "<ip>")]
    public void Ip_addresses_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, R.Redact(input));
    }

    [Theory]
    [InlineData("version 1.2.3")]
    [InlineData("value 999.1.1.1")]
    [InlineData("at 19:40:12.123 started")]
    [InlineData("std::string")]
    public void Things_that_only_look_like_addresses_are_kept(string input)
    {
        Assert.Equal(input, R.Redact(input));
    }

    [Fact]
    public void Empty_or_one_letter_names_are_ignored()
    {
        var r = new Redactor([("", "<x>"), ("a", "<y>")]);
        Assert.Equal("a cat", r.Redact("a cat"));
    }

    [Fact]
    public void ForThisMachine_hides_this_PCs_name_and_user()
    {
        var text = $"{Environment.MachineName} {Environment.UserName}";
        var redacted = Redactor.ForThisMachine().Redact(text);
        Assert.DoesNotContain(Environment.MachineName, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, redacted, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~RedactorTests`
Expected: build FAILS — `CouchLink.Core.Diagnostics` / `Redactor` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Diagnostics/Redactor.cs`:
```csharp
using System.Text.RegularExpressions;

namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Removes personal data from text that will be posted publicly: given names
/// (PC name, user name) and IPv4/IPv6 addresses.
/// </summary>
public sealed class Redactor
{
    private const string IpPlaceholder = "<ip>";

    private static readonly Regex Ipv4 = new(
        @"(?<!\d)(?<!\d\.)(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\.?\d)",
        RegexOptions.Compiled);

    // Candidate IPv6; only replaced when it has "::" or all 7 colons, so times like 19:40:12 survive.
    private static readonly Regex Ipv6Candidate = new(
        @"(?<![\w:])[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}(?![\w:])",
        RegexOptions.Compiled);

    private readonly (Regex Pattern, string Placeholder)[] _names;

    public Redactor(IEnumerable<(string Value, string Placeholder)> names)
    {
        _names = names
            .Where(n => n.Value.Length >= 2)
            .Select(n => (new Regex(Regex.Escape(n.Value), RegexOptions.IgnoreCase), n.Placeholder))
            .ToArray();
    }

    public static Redactor ForThisMachine() =>
        new([(Environment.MachineName, "<host>"), (Environment.UserName, "<user>")]);

    public string Redact(string text)
    {
        foreach (var (pattern, placeholder) in _names)
            text = pattern.Replace(text, placeholder);
        text = Ipv4.Replace(text, IpPlaceholder);
        text = Ipv6Candidate.Replace(text, m =>
            m.Value.Contains("::") || m.Value.Count(c => c == ':') == 7 ? IpPlaceholder : m.Value);
        return text;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~RedactorTests`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): redactor for PC name, user name and IP addresses"
```

---

### Task 2: Rolling file log with in-memory tail

**Files:**
- Create: `src/CouchLink.Core/Diagnostics/FileLog.cs`
- Test: `tests/CouchLink.Core.Tests/FileLogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class FileLog(string directory, TimeProvider? time = null, long maxBytes = 1_000_000, int maxFiles = 5, int tailLines = 200)` with `string FilePath`, `void Write(string message)` (never throws, thread-safe), `IReadOnlyList<string> Tail()`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/FileLogTests.cs`:
```csharp
using CouchLink.Core.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "couchlink-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Write_appends_timestamped_line_to_file_and_tail()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 19, 40, 12, 123, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var log = new FileLog(_dir, time);

        log.Write("host started");

        Assert.Equal(["2026-10-05 19:40:12.123 host started"], log.Tail());
        Assert.Equal("2026-10-05 19:40:12.123 host started" + Environment.NewLine, File.ReadAllText(log.FilePath));
        Assert.Equal(Path.Combine(_dir, "couchlink.log"), log.FilePath);
    }

    [Fact]
    public void Tail_keeps_only_the_newest_lines()
    {
        var log = new FileLog(_dir, tailLines: 3);
        for (int i = 1; i <= 5; i++)
            log.Write($"line {i}");
        Assert.Equal(["line 3", "line 4", "line 5"], log.Tail().Select(l => l[24..]));
    }

    [Fact]
    public void Rolling_keeps_at_most_maxFiles_files()
    {
        var log = new FileLog(_dir, maxBytes: 100, maxFiles: 5);
        for (int i = 0; i < 60; i++)
            log.Write($"message number {i:D3} with some padding");

        var names = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["couchlink.1.log", "couchlink.2.log", "couchlink.3.log", "couchlink.4.log", "couchlink.log"], names);
        Assert.Contains("message number 059", File.ReadAllText(log.FilePath));
    }

    [Fact]
    public void Unwritable_directory_never_throws_and_tail_still_works()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
        File.WriteAllText(_dir, "a file where the log folder should be");
        try
        {
            var log = new FileLog(_dir);
            log.Write("still alive");
            Assert.EndsWith("still alive", log.Tail().Single());
        }
        finally
        {
            File.Delete(_dir);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~FileLogTests`
Expected: build FAILS — `FileLog` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Diagnostics/FileLog.cs`:
```csharp
namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Rolling text log (couchlink.log, couchlink.1.log ... couchlink.N.log) plus an
/// in-memory tail for crash reports. Never throws: if the disk fails, the tail
/// keeps working.
/// </summary>
public sealed class FileLog
{
    private const string BaseName = "couchlink";

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly int _tailLines;
    private readonly Queue<string> _tail = new();
    private readonly Lock _gate = new();

    public FileLog(string directory, TimeProvider? time = null, long maxBytes = 1_000_000, int maxFiles = 5, int tailLines = 200)
    {
        _directory = directory;
        _time = time ?? TimeProvider.System;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        _tailLines = tailLines;
        FilePath = Path.Combine(directory, BaseName + ".log");
    }

    public string FilePath { get; }

    public void Write(string message)
    {
        var line = $"{_time.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (_gate)
        {
            _tail.Enqueue(line);
            while (_tail.Count > _tailLines)
                _tail.Dequeue();

            try
            {
                Directory.CreateDirectory(_directory);
                RollIfNeeded();
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Disk problems must never take the app down; the tail still has the line.
            }
        }
    }

    public IReadOnlyList<string> Tail()
    {
        lock (_gate)
            return _tail.ToArray();
    }

    private void RollIfNeeded()
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length < _maxBytes)
            return;

        var oldest = Numbered(_maxFiles - 1);
        if (File.Exists(oldest))
            File.Delete(oldest);
        for (int i = _maxFiles - 2; i >= 1; i--)
        {
            var source = Numbered(i);
            if (File.Exists(source))
                File.Move(source, Numbered(i + 1));
        }
        File.Move(FilePath, Numbered(1));
    }

    private string Numbered(int index) => Path.Combine(_directory, $"{BaseName}.{index}.log");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FileLogTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): rolling file log with in-memory tail"
```

---

### Task 3: Crash report builder

**Files:**
- Create: `src/CouchLink.Core/Diagnostics/CrashContext.cs`, `src/CouchLink.Core/Diagnostics/ProjectLinks.cs`, `src/CouchLink.Core/Diagnostics/CrashReportBuilder.cs`
- Test: `tests/CouchLink.Core.Tests/CrashReportBuilderTests.cs`

**Interfaces:**
- Consumes: `Redactor` (Task 1).
- Produces:
  - `sealed record CrashContext(string AppVersion, DateTimeOffset Time, TimeSpan Uptime, string Windows, string Runtime, string Cpu, long RamBytes, string Gpu, string ViGEmBus, string Mode)`.
  - `static class ProjectLinks { const string NewIssue = "https://github.com/enriquezchristopher/CouchLink/issues/new"; }`.
  - `static string CrashReportBuilder.Build(CrashContext context, Exception exception, IReadOnlyList<string> logTail, Redactor redactor)`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/CrashReportBuilderTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CrashReportBuilderTests`
Expected: build FAILS — `CrashContext`, `CrashReportBuilder`, `ProjectLinks` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Diagnostics/CrashContext.cs`:
```csharp
namespace CouchLink.Core.Diagnostics;

/// <summary>Facts about the app and machine at crash time. Must not contain personal data.</summary>
public sealed record CrashContext(
    string AppVersion,
    DateTimeOffset Time,
    TimeSpan Uptime,
    string Windows,
    string Runtime,
    string Cpu,
    long RamBytes,
    string Gpu,
    string ViGEmBus,
    string Mode);
```

`src/CouchLink.Core/Diagnostics/ProjectLinks.cs`:
```csharp
namespace CouchLink.Core.Diagnostics;

public static class ProjectLinks
{
    public const string NewIssue = "https://github.com/enriquezchristopher/CouchLink/issues/new";
}
```

`src/CouchLink.Core/Diagnostics/CrashReportBuilder.cs`:
```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CrashReportBuilderTests`
Expected: PASS (15 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): crash report builder with redacted exception and log tail"
```

---

### Task 4: Crash report store

**Files:**
- Create: `src/CouchLink.Core/Diagnostics/CrashReportStore.cs`
- Test: `tests/CouchLink.Core.Tests/CrashReportStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class CrashReportStore(string primaryDirectory, string fallbackDirectory)` with `const int Keep = 20`, `string PrimaryDirectory`, `static CrashReportStore Default()`, `string Save(string content, DateTime localNow)` (throws `IOException` only if both folders fail), `IReadOnlyList<string> Unshown()`, `void MarkShown(string path)` (never throws).

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/CrashReportStoreTests.cs`:
```csharp
using CouchLink.Core.Diagnostics;

namespace CouchLink.Core.Tests;

public sealed class CrashReportStoreTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 5, 19, 40, 12);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "couchlink-tests", Guid.NewGuid().ToString("N"));
    private string Primary => Path.Combine(_root, "primary");
    private string Fallback => Path.Combine(_root, "fallback");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Block(string directory)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(directory, "a file where the folder should be");
    }

    [Fact]
    public void Save_writes_named_file_in_primary_folder()
    {
        var store = new CrashReportStore(Primary, Fallback);
        var path = store.Save("report body", T0);
        Assert.Equal(Path.Combine(Primary, "couchlink-crash-20261005-194012.txt"), path);
        Assert.Equal("report body", File.ReadAllText(path));
    }

    [Fact]
    public void Two_crashes_in_the_same_second_get_separate_files()
    {
        var store = new CrashReportStore(Primary, Fallback);
        var a = store.Save("a", T0);
        var b = store.Save("b", T0);
        Assert.NotEqual(a, b);
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public void Only_the_newest_20_reports_are_kept()
    {
        var store = new CrashReportStore(Primary, Fallback);
        for (int i = 0; i < 25; i++)
            store.Save($"r{i}", T0.AddMinutes(i));
        var files = Directory.GetFiles(Primary, "couchlink-crash-*.txt").Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(CrashReportStore.Keep, files.Length);
        Assert.Equal("couchlink-crash-20261005-194512.txt", files[0]); // i = 5 is the oldest kept
    }

    [Fact]
    public void Unwritable_primary_falls_back()
    {
        Block(Primary);
        var path = new CrashReportStore(Primary, Fallback).Save("x", T0);
        Assert.StartsWith(Fallback, path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Both_unwritable_throws_IOException()
    {
        Block(Primary);
        Block(Fallback);
        Assert.Throws<IOException>(() => new CrashReportStore(Primary, Fallback).Save("x", T0));
    }

    [Fact]
    public void Unshown_lists_reports_until_marked_shown()
    {
        var store = new CrashReportStore(Primary, Fallback);
        var a = store.Save("a", T0);
        var b = store.Save("b", T0.AddMinutes(1));
        Assert.Equal([a, b], store.Unshown());

        store.MarkShown(a);
        Assert.Equal([b], store.Unshown());

        var reopened = new CrashReportStore(Primary, Fallback);
        Assert.Equal([b], reopened.Unshown());
    }

    [Fact]
    public void Unshown_includes_fallback_reports_and_is_empty_with_no_folders()
    {
        Assert.Empty(new CrashReportStore(Primary, Fallback).Unshown());
        Block(Primary);
        var path = new CrashReportStore(Primary, Fallback).Save("x", T0);
        Assert.Equal([path], new CrashReportStore(Primary, Fallback).Unshown());
    }

    [Fact]
    public void MarkShown_never_throws()
    {
        new CrashReportStore(Primary, Fallback).MarkShown(Path.Combine(_root, "missing", "nope.txt"));
    }

    [Fact]
    public void Default_uses_LocalAppData_and_Temp()
    {
        var store = CrashReportStore.Default();
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchLink", "CrashReports"),
            store.PrimaryDirectory);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CrashReportStoreTests`
Expected: build FAILS — `CrashReportStore` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Diagnostics/CrashReportStore.cs`:
```csharp
namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Saves crash reports where the user can find them, falls back to %TEMP% when
/// that fails, keeps the newest <see cref="Keep"/>, and remembers which ones the
/// user has already been shown.
/// </summary>
public sealed class CrashReportStore
{
    public const int Keep = 20;
    private const string Pattern = "couchlink-crash-*.txt";
    private const string ShownIndex = "shown.txt";

    private readonly string _fallback;

    public CrashReportStore(string primaryDirectory, string fallbackDirectory)
    {
        PrimaryDirectory = primaryDirectory;
        _fallback = fallbackDirectory;
    }

    public string PrimaryDirectory { get; }

    public static CrashReportStore Default() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchLink", "CrashReports"),
        Path.Combine(Path.GetTempPath(), "CouchLink", "CrashReports"));

    /// <summary>Writes the report and returns its full path. Throws IOException only if both folders fail.</summary>
    public string Save(string content, DateTime localNow)
    {
        var name = $"couchlink-crash-{localNow:yyyyMMdd-HHmmss}";
        Exception? firstError = null;
        foreach (var directory in new[] { PrimaryDirectory, _fallback })
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = UniquePath(directory, name);
                File.WriteAllText(path, content);
                Prune(directory);
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                firstError ??= e;
            }
        }
        throw new IOException($"Could not save the crash report: {firstError?.Message}", firstError);
    }

    /// <summary>Reports the user has not been shown yet, oldest first.</summary>
    public IReadOnlyList<string> Unshown()
    {
        var result = new List<string>();
        foreach (var directory in new[] { PrimaryDirectory, _fallback })
        {
            if (!Directory.Exists(directory))
                continue;
            var shown = ReadShown(directory);
            result.AddRange(Directory.GetFiles(directory, Pattern)
                .Where(f => !shown.Contains(Path.GetFileName(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal));
        }
        return result;
    }

    public void MarkShown(string path)
    {
        try
        {
            var index = Path.Combine(Path.GetDirectoryName(path)!, ShownIndex);
            File.AppendAllText(index, Path.GetFileName(path) + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Worst case the dialog shows again next start; never crash over it.
        }
    }

    private static string UniquePath(string directory, string name)
    {
        var path = Path.Combine(directory, name + ".txt");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(directory, $"{name}-{n}.txt");
        return path;
    }

    private static void Prune(string directory)
    {
        var files = Directory.GetFiles(directory, Pattern)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
        foreach (var old in files.Take(Math.Max(0, files.Length - Keep)))
            File.Delete(old);
    }

    private static HashSet<string> ReadShown(string directory)
    {
        var index = Path.Combine(directory, ShownIndex);
        return File.Exists(index) ? File.ReadAllLines(index).ToHashSet() : [];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CrashReportStoreTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(core): crash report store with fallback, pruning and shown tracking"
```

---

### Task 5: Wire crash handling into the app and verify end to end

**Files:**
- Create: `src/CouchLink.App/AppServices.cs`, `src/CouchLink.App/Diagnostics/SystemInfo.cs`, `src/CouchLink.App/Diagnostics/CrashDialog.cs`, `src/CouchLink.App/Diagnostics/CrashHandler.cs`
- Modify: `src/CouchLink.App/App.xaml`, `src/CouchLink.App/App.xaml.cs`, `src/CouchLink.App/MainWindow.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`, `src/CouchLink.App/HostInputService.cs`

**Interfaces:**
- Consumes: `FileLog` (Task 2), `CrashContext`, `CrashReportBuilder`, `ProjectLinks` (Task 3), `CrashReportStore` (Task 4), `Redactor` (Task 1).
- Produces: crash handling for the whole app; `AppServices.Log`, `AppServices.CrashReports`, `AppServices.DescribeMode` for later plans.

Windows glue (WPF, registry, driver files, global exception hooks): verified end to end by the UI Automation script in Step 8 instead of unit tests.

- [ ] **Step 1: Shared services**

`src/CouchLink.App/AppServices.cs`:
```csharp
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
}
```

- [ ] **Step 2: System information**

`src/CouchLink.App/Diagnostics/SystemInfo.cs`:
```csharp
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CouchLink.Core.Diagnostics;
using Microsoft.Win32;

namespace CouchLink.App.Diagnostics;

/// <summary>Collects crash-report facts. Every probe is guarded: a failure becomes "unknown".</summary>
internal static class SystemInfo
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static CrashContext Collect(string mode) => new(
        AppVersion: Probe(AppVersion),
        Time: DateTimeOffset.Now,
        Uptime: DateTime.Now - AppServices.Started,
        Windows: Probe(() => RuntimeInformation.OSDescription),
        Runtime: Probe(() => RuntimeInformation.FrameworkDescription),
        Cpu: Probe(Cpu),
        RamBytes: GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Gpu: Probe(Gpu),
        ViGEmBus: Probe(ViGEmBus),
        Mode: Probe(() => mode));

    private static string AppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SystemInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private static string Cpu()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var name = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "unknown CPU";
        return $"{name} ({Environment.ProcessorCount} threads)";
    }

    private static string Gpu()
    {
        using var display = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (display is null)
            return "unknown";
        var adapters = new List<string>();
        foreach (var sub in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            using var adapter = display.OpenSubKey(sub);
            if (adapter?.GetValue("DriverDesc") is string desc)
                adapters.Add($"{desc} (driver {adapter.GetValue("DriverVersion") ?? "?"})");
        }
        return adapters.Count > 0 ? string.Join("; ", adapters.Distinct()) : "unknown";
    }

    private static string ViGEmBus()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "ViGEmBus.sys");
        return File.Exists(path)
            ? $"installed, version {FileVersionInfo.GetVersionInfo(path).FileVersion ?? "?"}"
            : "not installed";
    }

    private static string Probe(Func<string> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception e)
        {
            return $"unknown ({e.GetType().Name})";
        }
    }
}
```

- [ ] **Step 3: Crash dialog**

`src/CouchLink.App/Diagnostics/CrashDialog.cs`:
```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App.Diagnostics;

/// <summary>Tells the user where the crash report is and how to send it.</summary>
internal sealed class CrashDialog : Window
{
    private CrashDialog(string heading, string? reportPath, string? saveError)
    {
        Title = "CouchLink crashed";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        if (reportPath is not null)
        {
            panel.Children.Add(new TextBlock { Text = "A report was saved to:", Margin = new Thickness(0, 8, 0, 4) });
            var pathBox = new TextBox { Text = reportPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(pathBox, "ReportPath");
            panel.Children.Add(pathBox);
            panel.Children.Add(new TextBlock
            {
                Text = "Please attach this file to a new issue so we can fix it.",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            var error = new TextBlock
            {
                Text = $"The crash report could not be saved: {saveError}",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetAutomationId(error, "SaveError");
            panel.Children.Add(error);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(MakeButton("Open folder", "OpenFolderButton", reportPath is not null, () =>
            Process.Start("explorer.exe", $"/select,\"{reportPath}\"")));
        buttons.Children.Add(MakeButton("Copy path", "CopyPathButton", reportPath is not null, () =>
            Clipboard.SetText(reportPath!)));
        buttons.Children.Add(MakeButton("Report on GitHub", "ReportButton", true, () =>
            Process.Start(new ProcessStartInfo(ProjectLinks.NewIssue) { UseShellExecute = true })));
        buttons.Children.Add(MakeButton("Close", "CloseButton", true, Close));
        panel.Children.Add(buttons);

        Content = panel;
    }

    /// <summary>Shows the dialog modally on its own STA thread and waits. Never throws.</summary>
    public static void ShowAndWait(string heading, string? reportPath, string? saveError)
    {
        var thread = new Thread(() =>
        {
            try
            {
                new CrashDialog(heading, reportPath, saveError).ShowDialog();
            }
            catch
            {
                // Nothing left to tell the user with; the report file is already written.
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static Button MakeButton(string text, string automationId, bool enabled, Action onClick)
    {
        var button = new Button { Content = text, MinWidth = 110, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsEnabled = enabled };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            try
            {
                onClick();
            }
            catch
            {
                // e.g. clipboard busy or no browser; the path is still on screen.
            }
        };
        return button;
    }
}
```

- [ ] **Step 4: Crash handler**

`src/CouchLink.App/Diagnostics/CrashHandler.cs`:
```csharp
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
```

- [ ] **Step 5: Startup in code**

`src/CouchLink.App/App.xaml` (replace contents; startup moves to code so crash handling is installed before any window):
```xml
<Application x:Class="CouchLink.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources/>
</Application>
```

`src/CouchLink.App/App.xaml.cs` (replace contents):
```csharp
using System.Windows;
using System.Windows.Threading;
using CouchLink.App.Diagnostics;

namespace CouchLink.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashHandler.Install(this); // first, so even startup crashes are reported
        base.OnStartup(e);
        AppServices.Log.Write($"CouchLink started (args: {string.Join(' ', e.Args)})");

        var crashTest = e.Args.FirstOrDefault(a => a.StartsWith("--crash-test=", StringComparison.Ordinal))?["--crash-test=".Length..];
        if (crashTest == "startup")
            throw new InvalidOperationException("Crash test: startup");

        CrashHandler.ShowPendingReports();
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

    /// <summary>Deliberate crashes for verifying crash reports: --crash-test=ui|background|double|startup.</summary>
    private static void RunCrashTest(string kind)
    {
        switch (kind)
        {
            case "ui":
                throw new InvalidOperationException("Crash test: UI thread");
            case "background":
                new Thread(() => throw new InvalidOperationException("Crash test: background thread")).Start();
                break;
            case "double":
                new Thread(() => throw new InvalidOperationException("Crash test: double A")).Start();
                new Thread(() => throw new InvalidOperationException("Crash test: double B")).Start();
                break;
        }
    }
}
```

- [ ] **Step 6: Main window: Crash reports button, mode, logging**

In `src/CouchLink.App/MainWindow.xaml`, after the `StatusText` TextBlock add:
```xml
        <Button x:Name="CrashReportsButton" Content="Crash reports" Height="28" Margin="0,16,0,0"
                HorizontalAlignment="Left" Padding="12,0" Click="OnOpenCrashReports"/>
```

In `src/CouchLink.App/MainWindow.xaml.cs`:
- add `using System.Diagnostics;` at the top;
- at the end of `OnHost` (after the buttons are disabled) add:
```csharp
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
```
- at the end of `OnJoin` add:
```csharp
        var slot = (int)SlotBox.SelectedItem;
        AppServices.DescribeMode = () => $"Client (slot P{slot})";
        AppServices.Log.Write($"Joined as P{slot}");
```
- add the handler:
```csharp
    private void OnOpenCrashReports(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppServices.CrashReports.PrimaryDirectory);
        Process.Start("explorer.exe", $"\"{AppServices.CrashReports.PrimaryDirectory}\"");
    }
```

In `src/CouchLink.App/HostInputService.cs`, make `OnError` also log:
```csharp
    private void OnError(Exception e)
    {
        LastError = $"{e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Pad error: {e}");
    }
```

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 8: End-to-end crash check (UI Automation)**

Save as `$env:TEMP\crash-check.ps1` and run `powershell -ExecutionPolicy Bypass -File $env:TEMP\crash-check.ps1 <kind>` for each kind `ui`, `background`, `double`, `startup`:
```powershell
param([string]$Kind)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = 'C:\dev\CouchLink\src\CouchLink.App\bin\Debug\net10.0-windows\CouchLink.App.exe'
$dir = Join-Path $env:LOCALAPPDATA 'CouchLink\CrashReports'
$before = @(Get-ChildItem $dir -Filter 'couchlink-crash-*.txt' -ErrorAction SilentlyContinue).Count
$p = Start-Process $exe -ArgumentList "--crash-test=$Kind" -PassThru
$root = [Windows.Automation.AutomationElement]::RootElement
$cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, 'CouchLink crashed')
$dlg = $null
for ($i = 0; $i -lt 60 -and -not $dlg; $i++) { Start-Sleep -Milliseconds 250; $dlg = $root.FindFirst([Windows.Automation.TreeScope]::Children, $cond) }
if (-not $dlg) { "FAIL: no crash dialog"; Stop-Process -Id $p.Id -Force; exit 1 }
function Find($id) { $dlg.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))) }
$path = (Find 'ReportPath').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
"dialog path: $path"
(Find 'CloseButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
if (-not $p.WaitForExit(10000)) { "FAIL: app did not exit"; Stop-Process -Id $p.Id -Force; exit 1 }
$after = @(Get-ChildItem $dir -Filter 'couchlink-crash-*.txt').Count
$text = Get-Content $path -Raw
"exit code: $($p.ExitCode)  new reports: $($after - $before)"
"has exception: $($text -match 'Crash test')"
"has no PC name: $(-not ($text -match [regex]::Escape($env:COMPUTERNAME)))"
"has no user name: $(-not ($text -match [regex]::Escape($env:USERNAME)))"
```
Expected for every kind: a dialog appears with the full report path; after **Close** the app exits with code 1; `new reports: 1` (exactly one, also for `double`); `has exception: True`; `has no PC name: True`; `has no user name: True`.

Then start the app normally (`CouchLink.App.exe` with no arguments): no "crashed last time" dialog appears (all reports were marked shown).

- [ ] **Step 9: Pending-report check**

1. Run `CouchLink.App.exe --crash-test=background`, and when the dialog appears end the process from Task Manager (or `Stop-Process`) without clicking Close.
2. Start `CouchLink.App.exe` normally.
Expected: a dialog headed **"CouchLink crashed last time."** shows that report's path; after Close the main window opens; starting again shows no dialog.

- [ ] **Step 10: Commit**

```powershell
git add .
git commit -m "feat(app): crash reports with dialog, pending-report notice and crash tests"
```
