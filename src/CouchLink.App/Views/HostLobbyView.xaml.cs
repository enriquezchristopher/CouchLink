using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Ui;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App.Views;

/// <summary>
/// "Hosting on PC-03": stream settings (resolution, frame rate, quality) with a warning when the
/// network link is too slow, Let everyone in, the players with Kick, Stop hosting (which asks first
/// when players are in), and Stream stats. Owns the <see cref="HostService"/> and the approval popups.
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
        {
            var item = new RadioButton
            {
                Content = StreamQualities.Label(quality),
                Tag = quality,
                GroupName = "Quality",
                Style = (Style)FindResource("SegmentedItem"),
                IsChecked = quality == StreamSettings.Default.Quality,
            };
            AutomationProperties.SetAutomationId(item, $"Quality{quality}");
            QualityGroup.Children.Add(item);
        }
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
        foreach (var item in QualityGroup.Children.OfType<RadioButton>())
            item.Checked += (_, _) => _host?.ChangeSettings(CurrentSettings());
        AllowEveryoneBox.Click += (_, _) =>
        {
            if (_host is not null)
                _host.AllowEveryone = AllowEveryoneBox.IsChecked == true;
        };
        StopButton.Click += (_, _) => Stop();
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
        RefreshPlayers();
        UpdateDetails();
        _details.Start();
        return true;
    }

    internal StreamSettings CurrentSettings() => new(
        (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
        (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag,
        (StreamQuality)QualityGroup.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag);

    private void Stop()
    {
        var names = _host?.Players.Select(p => p.Name).ToList() ?? [];
        if (StopHostingPrompt.For(names) is { } message
            && !ThemedDialog.Confirm(Window.GetWindow(this), "Stop hosting?", message, "Stop hosting", "Keep hosting", danger: true))
            return;
        Dispose();
        Stopped?.Invoke();
    }

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
        if (_host is not null)
            ShowPlayers(_host.Players);
    }

    internal void ShowPlayers(IReadOnlyList<PlayerInfo> players)
    {
        PlayersHeading.Text = $"PLAYERS {players.Count + 1} / {HostSession.Capacity + 1}";
        PlayerList.Children.Clear();
        PlayerList.Children.Add(PlayerRow(1, "You (host)", reconnecting: false, kickable: false));
        if (players.Count == 0)
        {
            PlayerList.Children.Add(new TextBlock
            {
                Text = "No one has joined yet. Players appear here when they join.",
                Style = (Style)FindResource("CaptionText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        foreach (var player in players)
            PlayerList.Children.Add(PlayerRow(player.Slot, player.Name, player.State == PlayerState.Reserved, kickable: true));
    }

    private DockPanel PlayerRow(byte slot, string name, bool reconnecting, bool kickable)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (kickable)
        {
            var kick = new Button { Content = "Kick", Style = (Style)FindResource("GhostButton") };
            AutomationProperties.SetName(kick, $"Kick {name}");
            AutomationProperties.SetAutomationId(kick, $"Kick{slot}");
            kick.Click += (_, _) => _host?.Kick(slot);
            DockPanel.SetDock(kick, Dock.Right);
            row.Children.Add(kick);
        }
        if (reconnecting)
        {
            var pill = new StatusPill { Kind = PillKind.Reconnecting, Text = "Reconnecting", Margin = new Thickness(8, 0, 4, 0) };
            DockPanel.SetDock(pill, Dock.Right);
            row.Children.Add(pill);
        }
        var chip = new PlayerChip { Slot = slot };
        DockPanel.SetDock(chip, Dock.Left);
        row.Children.Add(chip);
        row.Children.Add(new TextBlock
        {
            Text = name,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
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
