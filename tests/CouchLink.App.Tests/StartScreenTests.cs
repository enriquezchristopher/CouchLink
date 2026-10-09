using System.Windows;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Protocol;

namespace CouchLink.App.Tests;

public class StartScreenTests
{
    [Fact]
    public void Start_shows_this_pc_and_the_version()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var start = new StartView();
            Assert.Equal($"This PC: {PcName.ThisPc}", start.PcText.Text);
            Assert.Equal($"v{AppInfo.Version}", start.VersionText.Text);
            Assert.Equal("Host a game", start.HostButton.Content);
            Assert.Equal("Join a game", start.JoinButton.Content);
        });
    }

    [Fact]
    public void The_main_window_opens_on_start_with_the_header()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var window = new MainWindow();
            Assert.IsType<StartView>(window.Screen.Content);
            Assert.True(window.Header.ControlsButton.Focusable);
            Assert.Equal(540, window.Width);
            Assert.Equal(460, window.MinWidth);
            window.Close();
        });
    }
}
