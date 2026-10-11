using System.IO;
using CouchLink.App.Presentation;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public sealed class HostSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CouchLinkTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private HostSettings Open() => new(SettingsFile, _log.Add);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void DualShock_4_without_a_settings_file()
    {
        Assert.Equal(PadKind.DualShock4, Open().Controller);
        Assert.Empty(_log);
    }

    [Fact]
    public void The_choice_is_saved_and_read_back()
    {
        Open().Controller = PadKind.Xbox360;
        Assert.Equal(PadKind.Xbox360, Open().Controller);
        Assert.Contains("\"hostController\": \"xbox360\"", File.ReadAllText(SettingsFile));

        Open().Controller = PadKind.DualShock4;
        Assert.Contains("\"hostController\": \"dualshock4\"", File.ReadAllText(SettingsFile));
    }

    [Theory]
    [InlineData("{ \"hostController\": \"ps5\" }")]
    [InlineData("{ \"hostController\": 2 }")]
    [InlineData("{ not json")]
    public void A_bad_value_reads_as_DualShock_4_and_is_logged_once(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, text);
        Assert.Equal(PadKind.DualShock4, Open().Controller);
        Assert.Single(_log);
    }

    [Fact]
    public void Other_settings_are_kept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, "{ \"reduceMotion\": true }");
        Open().Controller = PadKind.Xbox360;
        Assert.Contains("\"reduceMotion\": true", File.ReadAllText(SettingsFile));
        Assert.True(new MotionSettings(SettingsFile, _log.Add).ReduceMotion);
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_choice_for_this_run_and_logs()
    {
        Directory.CreateDirectory(SettingsFile); // a folder where the file should be
        var settings = Open();
        settings.Controller = PadKind.Xbox360;
        Assert.Equal(PadKind.Xbox360, settings.Controller);
        Assert.NotEmpty(_log);
    }
}
