using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Views;

namespace CouchLink.App;

/// <summary>The app's one window: shows the Start screen, the host lobby, the join list or the session.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowStart();
    }

    /// <summary>Shows a view; the one it replaces is disposed (stopping whatever it owned).</summary>
    private void Show(UserControl view)
    {
        if (Screen.Content is IDisposable old && !ReferenceEquals(old, view))
            old.Dispose();
        Screen.Content = view;
    }

    private void ShowStart()
    {
        var start = new StartView();
        start.HostClicked += ShowHost;
        start.CrashReportsClicked += OpenCrashReports;
        Show(start);
        AppServices.DescribeMode = () => "Idle";
    }

    private void ShowHost()
    {
        var lobby = new HostLobbyView();
        if (!lobby.TryStart(out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        lobby.Stopped += ShowStart;
        Show(lobby);
    }

    private void OpenCrashReports()
    {
        try
        {
            var directory = AppServices.CrashReports.ReportsDirectory();
            Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the crash reports folder:\n{ex.Message}", "CouchLink");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        (Screen.Content as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
