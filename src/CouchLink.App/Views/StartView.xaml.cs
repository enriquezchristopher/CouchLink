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
        CrashReportsButton.Click += (_, _) => CrashReportsClicked?.Invoke();
    }

    public event Action? HostClicked;
    public event Action? JoinClicked;
    public event Action? CrashReportsClicked;
}
