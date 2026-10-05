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
        var tail = log.Tail();
        Assert.Equal(3, tail.Count);
        Assert.Equal("line 3", (string)tail[0].Substring(24));
        Assert.Equal("line 4", (string)tail[1].Substring(24));
        Assert.Equal("line 5", (string)tail[2].Substring(24));
    }

    [Fact]
    public void Rolling_keeps_at_most_maxFiles_files()
    {
        var log = new FileLog(_dir, maxBytes: 100, maxFiles: 5);
        for (int i = 0; i < 60; i++)
            log.Write($"message number {i:D3} with some padding");

        var names = Directory.GetFiles(_dir).Select(f => Path.GetFileName(f)).Order().ToArray()!;
        var expected = new[] { "couchlink.1.log", "couchlink.2.log", "couchlink.3.log", "couchlink.4.log", "couchlink.log" };
        Assert.Equal(expected, names);
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
