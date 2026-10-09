using System.Windows.Controls;
using CouchLink.App.Presentation;
using CouchLink.Core.Protocol;

namespace CouchLink.App.Views;

/// <summary>The first screen: Host a game and Join a game, with this PC's name and the version.</summary>
internal sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
        PcText.Text = $"This PC: {PcName.ThisPc}";
        VersionText.Text = $"v{AppInfo.Version}";
        HostButton.Click += (_, _) => HostClicked?.Invoke();
        JoinButton.Click += (_, _) => JoinClicked?.Invoke();
    }

    public event Action? HostClicked;
    public event Action? JoinClicked;
}
