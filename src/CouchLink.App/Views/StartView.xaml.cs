using System.Windows.Controls;

namespace CouchLink.App.Views;

/// <summary>The first screen: big Host and Join buttons.</summary>
internal sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
        HostButton.Click += (_, _) => HostClicked?.Invoke();
        JoinButton.Click += (_, _) => JoinClicked?.Invoke();
        ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();
        CrashReportsButton.Click += (_, _) => CrashReportsClicked?.Invoke();
    }

    public event Action? HostClicked;
    public event Action? JoinClicked;
    public event Action? ControlsClicked;
    public event Action? CrashReportsClicked;
}
