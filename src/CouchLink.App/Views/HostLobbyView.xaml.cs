using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App.Views;

/// <summary>
/// "Hosting on PC-03": stream settings (resolution, frame rate, quality) with a warning when the
/// network link is too slow, Allow everyone, the players with Kick, Stop hosting, and a
/// Details section with the dev stats. Owns the <see cref="HostService"/> and the approval popups.
/// </summary>
internal sealed partial class HostLobbyView : UserControl, IHostUi, IDisposable
{
    private readonly DispatcherTimer _details = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<int, ApprovalPopup> _asks = [];
    private HostService? _host;

    public HostLobbyView()
    {
        InitializeComponent();
        Heading.Text = $"Hosting on {PcName.ThisPc}";
        foreach (var resolution in StreamSettings.Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = StreamSettings.Label(resolution), Tag = resolution });
        ResolutionBox.SelectedIndex = StreamSettings.Resolutions.ToList().IndexOf(StreamSettings.Default.Resolution);
        foreach (int rate in StreamSettings.FrameRatesFor(DisplayInfo.PrimaryRefreshRate()))
            FrameRateBox.Items.Add(new ComboBoxItem { Content = $"{rate} fps", Tag = rate });
        FrameRateBox.SelectedIndex = 0; // 60
        foreach (var quality in StreamQualities.All)
            QualityBox.Items.Add(new ComboBoxItem { Content = StreamQualities.Label(quality), Tag = quality });
        QualityBox.SelectedIndex = StreamQualities.All.ToList().IndexOf(StreamSettings.Default.Quality);
        _details.Tick += (_, _) => UpdateDetails();
    }

    /// <summary>The host clicked Stop hosting; the view has already cleaned up.</summary>
    public event Action? Stopped;

    public bool TryStart(out string? error)
    {
        if (!HostService.TryStart(CurrentSettings(), this, out _host, out error))
            return false;
        ResolutionBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        FrameRateBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        QualityBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        AllowEveryoneBox.Click += (_, _) =>
        {
            if (_host is not null)
                _host.AllowEveryone = AllowEveryoneBox.IsChecked == true;
        };
        StopButton.Click += (_, _) =>
        {
            Dispose();
            Stopped?.Invoke();
        };
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
        RefreshPlayers();
        UpdateDetails();
        _details.Start();
        return true;
    }

    private StreamSettings CurrentSettings() => new(
        (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
        (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag,
        (StreamQuality)((ComboBoxItem)QualityBox.SelectedItem).Tag);

    void IHostUi.PlayersChanged() => Dispatcher.InvokeAsync(RefreshPlayers);

    void IHostUi.AskHost(int connection, string name) => Dispatcher.InvokeAsync(() => Ask(connection, name));

    void IHostUi.CloseAsk(int connection) => Dispatcher.InvokeAsync(() =>
    {
        if (_asks.Remove(connection, out var popup))
            popup.CloseByHost();
    });

    private void Ask(int connection, string name)
    {
        if (_host is not { } host)
            return;
        var popup = new ApprovalPopup(name, _asks.Count, () => host.Allow(connection), () => host.Deny(connection));
        _asks[connection] = popup;
        popup.Closed += (_, _) => _asks.Remove(connection);
        popup.Show();
    }

    private void RefreshPlayers()
    {
        if (_host is null)
            return;
        var players = _host.Players;
        PlayersHeading.Text = $"Players: {players.Count + 1}/{HostSession.Capacity + 1} (you are P1)";
        PlayerList.Children.Clear();
        if (players.Count == 0)
        {
            PlayerList.Children.Add(new TextBlock
            {
                Text = "No one has joined yet.",
                Foreground = Brushes.Gray,
            });
        }
        foreach (var player in players)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var kick = new Button { Content = "Kick", Padding = new Thickness(12, 2, 12, 2) };
            byte slot = player.Slot;
            kick.Click += (_, _) => _host?.Kick(slot);
            DockPanel.SetDock(kick, Dock.Right);
            row.Children.Add(kick);
            row.Children.Add(new TextBlock
            {
                Text = $"P{player.Slot}   {player.Name}" + (player.State == PlayerState.Reserved ? "   (reconnecting)" : ""),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
            });
            PlayerList.Children.Add(row);
        }
    }

    private void UpdateDetails()
    {
        if (_host is null)
            return;
        DetailsText.Text = $"Virtual pads: {_host.PadCount}\n{_host.DescribeStreams()}" +
            (_host.LastError is { } error ? $"\nLast error: {error}" : "");
        var warning = _host.LinkWarning();
        BudgetWarning.Text = warning ?? "";
        BudgetWarning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Dispose()
    {
        if (_host is not { } host)
            return;
        _host = null;
        _details.Stop();
        foreach (var popup in _asks.Values.ToList())
            popup.CloseByHost();
        _asks.Clear();
        host.Dispose(); // tells every client the session ended
        AppServices.DescribeMode = () => "Idle";
        AppServices.Log.Write("Hosting stopped");
    }
}
