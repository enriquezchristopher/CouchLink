namespace CouchLink.Core.Tests;

public class DevOptionsTests
{
    [Fact]
    public void Nothing_set_by_default()
    {
        Assert.Equal(new DevOptions(false, null), DevOptions.Parse([]));
    }

    [Fact]
    public void Test_pattern_and_save_video_are_read()
    {
        var options = DevOptions.Parse(["--crash-test=ui", "--test-pattern", @"--save-video=C:\temp\in.h264"]);
        Assert.True(options.TestPattern);
        Assert.Equal(@"C:\temp\in.h264", options.SaveVideoPath);
    }

    [Fact]
    public void An_empty_save_path_is_ignored()
    {
        Assert.Null(DevOptions.Parse(["--save-video="]).SaveVideoPath);
    }

    [Fact]
    public void Windowed_player_is_a_dev_switch()
    {
        Assert.True(DevOptions.Parse(["--windowed-player"]).WindowedPlayer);
        Assert.False(DevOptions.Parse([]).WindowedPlayer);
    }

    [Fact]
    public void Test_tone_and_audio_loss_are_dev_switches()
    {
        var options = DevOptions.Parse(["--test-tone", "--audio-loss=5"]);

        Assert.True(options.TestTone);
        Assert.Equal(5, options.AudioLossPercent);
        Assert.Equal(2.5, DevOptions.Parse(["--audio-loss=2.5"]).AudioLossPercent);
        Assert.Equal(100, DevOptions.Parse(["--audio-loss=250"]).AudioLossPercent);
        Assert.Equal(0, DevOptions.Parse(["--audio-loss=lots"]).AudioLossPercent);
        Assert.False(DevOptions.Parse([]).TestTone);
    }
}
