using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_has_three_parts()
    {
        Assert.Equal("1.9.0", AppInfo.Format(new Version(1, 9, 0, 0)));
        Assert.Equal("?", AppInfo.Format(null));
    }

    [Fact]
    public void Version_comes_from_the_app_assembly()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void About_text_has_version_and_license()
    {
        Assert.Contains($"Version {AppInfo.Version}", AppInfo.AboutText);
        Assert.Contains("GNU GPL v3", AppInfo.AboutText);
    }
}
