using System.IO;
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public sealed class MotionSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CouchLinkTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private MotionSettings Open() => new(SettingsFile, _log.Add);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Motion_is_on_when_there_is_no_settings_file()
    {
        Assert.False(Open().ReduceMotion);
        Assert.Empty(_log);
    }

    [Fact]
    public void Reduce_motion_is_saved_and_read_back()
    {
        Open().ReduceMotion = true;
        Assert.True(Open().ReduceMotion);
        Assert.Contains("\"reduceMotion\": true", File.ReadAllText(SettingsFile));

        Open().ReduceMotion = false;
        Assert.False(Open().ReduceMotion);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"reduceMotion\": \"yes\" }")]
    [InlineData("")]
    public void A_corrupt_file_reads_as_motion_on_and_is_logged(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, text);
        Assert.False(Open().ReduceMotion);
        Assert.Single(_log);
    }

    [Fact]
    public void Other_settings_in_the_file_are_kept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, "{ \"somethingElse\": 3 }");
        Open().ReduceMotion = true;
        Assert.Contains("\"somethingElse\": 3", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Changing_it_raises_Changed_once()
    {
        var settings = Open();
        int changed = 0;
        settings.Changed += () => changed++;
        settings.ReduceMotion = true;
        settings.ReduceMotion = true; // same value: nothing to tell
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_choice_for_this_run_and_logs()
    {
        Directory.CreateDirectory(SettingsFile); // a folder where the file should be
        var settings = Open();
        settings.ReduceMotion = true;
        Assert.True(settings.ReduceMotion);
        Assert.NotEmpty(_log);
    }
}
