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
}
