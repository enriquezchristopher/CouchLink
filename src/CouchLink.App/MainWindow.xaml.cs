using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.App.Input;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.App.Views;

namespace CouchLink.App;

/// <summary>
/// The app's one window: the Start screen, the host lobby, the join list, or the session view while
/// joining and playing (the fullscreen player sits in front of it).
/// </summary>
public partial class MainWindow : Window, IClientUi
{
    private readonly DispatcherTimer _detailsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ClientSessionService? _session;
    private ClientPlay? _play;
    private SessionView? _sessionView;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        WindowTheme.UseCustomChrome(this, 48);
        Header.ControlsClicked += () => ControlsWindow.Open(this);
        Header.CrashReportsClicked += OpenCrashReports;
        Header.AboutClicked += () => ThemedDialog.Alert(this, "About CouchLink", AppInfo.AboutText);
        _detailsTimer.Tick += (_, _) =>
        {
            if (_sessionView is not null)
                _sessionView.Details = _play?.Describe() ?? "";
        };
        ShowStart();
    }

    /// <summary>
    /// Shows a view; the one it replaces is disposed (stopping whatever it owned). The header's buttons
    /// can't take focus on the session screen, where Raw Input keys would press them.
    /// </summary>
    private void Show(UserControl view, bool headerFocusable = true)
    {
        if (Screen.Content is IDisposable old && !ReferenceEquals(old, view))
            old.Dispose();
        Screen.Content = view;
        Header.ButtonsFocusable = headerFocusable;
        Motion.Enter(view);
    }

    private void ShowStart()
    {
        var start = new StartView();
        start.HostClicked += ShowHost;
        start.JoinClicked += () => ShowJoinList(null);
        Show(start);
        AppServices.DescribeMode = () => "Idle";
    }

    private void ShowHost()
    {
        var lobby = new HostLobbyView();
        if (!lobby.TryStart(out var error))
        {
            ThemedDialog.Alert(this, "Couldn't start hosting", error ?? "Hosting could not start.");
            return;
        }
        lobby.Stopped += ShowStart;
        Show(lobby);
    }

    private void ShowJoinList(string? message)
    {
        var list = new JoinListView();
        list.BackClicked += ShowStart;
        list.JoinRequested += Join;
        list.ShowMessage(message);
        Show(list);
    }

    private void Join(IPAddress host, string hostName)
    {
        _sessionView = new SessionView();
        _sessionView.LeaveClicked += LeaveSession;
        _sessionView.ControlsClicked += () => ControlsWindow.Open(this);
        Show(_sessionView, headerFocusable: false); // closes the join list, freeing UDP 47800
        _session = new ClientSessionService(host, hostName, Dispatcher, this);
        UpdateSessionView();
        _detailsTimer.Start();
    }

    void IClientUi.StartPlaying(byte slot)
    {
        if (_session is not { } session || _closed)
            return;
        if (!ClientPlay.TryStart(this, session.Host, slot, () => Dispatcher.InvokeAsync(LeaveSession),
                () => session.PlayerStatus, () => Dispatcher.InvokeAsync(OpenControlsOverGame), out _play, out var error))
        {
            session.Fail(error!);
            return;
        }
        Keyboard.ClearFocus(); // Raw Input keys still reach a focused control
        AppServices.DescribeMode = () => $"Client (slot P{slot})";
        UpdateSessionView();
    }

    void IClientUi.Ended(string? message)
    {
        if (_closed)
            return;
        EndSession();
        ShowJoinList(message);
    }

    void IClientUi.StateChanged() => UpdateSessionView();

    private void UpdateSessionView()
    {
        if (_session is { } session && _sessionView is { } view)
            view.Show(session.State, session.HostName, session.Slot);
    }

    /// <summary>Cancel, Leave or Ctrl+Alt+Q: the session tells the host, then ends and calls Ended(null).</summary>
    private void LeaveSession() => _session?.Leave();

    private void EndSession()
    {
        _detailsTimer.Stop();
        _play?.Dispose();
        _play = null;
        _session?.Dispose();
        _session = null;
        _sessionView = null;
        AppServices.DescribeMode = () => "Idle";
        Activate();
    }

    /// <summary>A later launch asked for this copy: the game if one is playing, otherwise this window.</summary>
    internal void BringToFront()
    {
        if (_play is { PlayerWindow: not 0 } play)
        {
            NativeMethods.SetForegroundWindow(play.PlayerWindow);
            return;
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    /// <summary>Ctrl+Alt+C in the player: the editor on top of the game, and back to the game when it closes.</summary>
    private void OpenControlsOverGame()
    {
        if (_play is null || _closed)
            return;
        ControlsWindow.Open(this, overGame: true, closed: ReturnToGame);
    }

    private void ReturnToGame()
    {
        if (_play is { PlayerWindow: not 0 } play)
            NativeMethods.SetForegroundWindow(play.PlayerWindow);
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
            ThemedDialog.Alert(this, "Couldn't open crash reports", $"Could not open the crash reports folder:\n{ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _session?.Leave(); // tells the host now instead of after 5 s of silence
        EndSession();
        (Screen.Content as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
