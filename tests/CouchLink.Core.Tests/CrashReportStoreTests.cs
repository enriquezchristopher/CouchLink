using System.Globalization;
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
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
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
        var files = Directory.GetFiles(Primary, "couchlink-crash-*.txt").Select(f => Path.GetFileName(f)).Order().ToArray()!;
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

    [Fact]
    public void ReportsDirectory_is_primary_when_creatable()
    {
        Assert.Equal(Primary, new CrashReportStore(Primary, Fallback).ReportsDirectory());
        Assert.True(Directory.Exists(Primary));
    }

    [Fact]
    public void ReportsDirectory_falls_back_when_primary_is_blocked()
    {
        Block(Primary);
        var dir = new CrashReportStore(Primary, Fallback).ReportsDirectory();
        Assert.Equal(Fallback, dir);
        Assert.True(Directory.Exists(Fallback));
    }

    [Fact]
    public void Prune_failure_does_not_duplicate_the_report()
    {
        var store = new CrashReportStore(Primary, Fallback);
        for (int i = 0; i < 20; i++)
            store.Save($"r{i}", T0.AddMinutes(i));
        var oldest = Directory.GetFiles(Primary, "couchlink-crash-*.txt").Order().First();
        using var locked = new FileStream(oldest, FileMode.Open, FileAccess.Read, FileShare.None);

        var path = store.Save("new", T0.AddMinutes(30));

        Assert.StartsWith(Primary, path);
        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(Directory.Exists(Fallback) && Directory.GetFiles(Fallback, "couchlink-crash-*.txt").Length > 0);
    }

    [Fact]
    public void File_name_ignores_current_culture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var path = new CrashReportStore(Primary, Fallback).Save("x", T0);
            Assert.Equal("couchlink-crash-20261005-194012.txt", Path.GetFileName(path));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
