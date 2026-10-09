using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Net;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>
/// The hosts on the LAN as cards ("PC-03 · 4/10 players"), heard on UDP 47800 while this view is shown,
/// plus Join by address for when broadcasts don't get through. An error banner says why the last
/// session ended; after 10 s with no host, a card says what to check.
/// </summary>
internal sealed partial class JoinListView : UserControl, IDisposable
{
    private readonly HostList _hosts = new(TimeProvider.System);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _noHosts = new() { Interval = TimeSpan.FromSeconds(10) };
    private DiscoveryListener? _listener;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<FoundHost> _shown = [];

    public JoinListView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        MessageBar.CloseClicked += () => ShowMessage(null);
        AddressJoinButton.Click += (_, _) => JoinByAddress();
        AddressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                JoinByAddress();
        };
        _refresh.Tick += (_, _) => Refresh();
        _noHosts.Tick += (_, _) =>
        {
            _noHosts.Stop();
            ShowNoHostsHelp();
        };
        Loaded += (_, _) => StartListening();
        Unloaded += (_, _) => Dispose();
    }

    public event Action? BackClicked;
    public event Action<IPAddress, string>? JoinRequested;

    public void ShowMessage(string? message)
    {
        MessageBar.Text = message ?? "";
        MessageBar.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartListening()
    {
        if (_listener is not null)
            return;
        if (!DiscoveryListener.TryCreate(Ports.Discovery, out _listener, out var error))
        {
            ScanningRow.Visibility = Visibility.Collapsed;
            DiscoveryError.Text = $"{error} Join by address still works.";
            DiscoveryError.Visibility = Visibility.Visible;
            return;
        }
        _cts = new CancellationTokenSource();
        _ = _listener!.RunAsync((announce, from) => _hosts.Seen(announce, from), _cts.Token,
            e => AppServices.Log.Write($"Discovery error: {e}"));
        _refresh.Start();
        _noHosts.Start();
    }

    private void Refresh()
    {
        var hosts = _hosts.Current();
        if (!hosts.SequenceEqual(_shown))
            ShowHosts(hosts);
    }

    internal void ShowHosts(IReadOnlyList<FoundHost> hosts)
    {
        // Cards are rebuilt on every change; only hosts that weren't listed before come in, one after another.
        var before = _shown.Select(h => h.Address).ToHashSet();
        var arriving = new List<UIElement>();
        _shown = hosts;
        if (hosts.Count > 0)
            HelpCard.Visibility = Visibility.Collapsed;
        HostButtons.Children.Clear();
        foreach (var host in hosts)
        {
            var card = HostCard(host);
            HostButtons.Children.Add(card);
            if (!before.Contains(host.Address))
                arriving.Add(card);
        }
        Motion.Stagger(arriving);
    }

    internal void ShowNoHostsHelp()
    {
        if (_shown.Count == 0)
            HelpCard.Visibility = Visibility.Visible;
    }

    private Button HostCard(FoundHost host)
    {
        int players = host.Players + 1, capacity = host.Capacity + 1;
        var card = new Button { Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(StyleProperty, "HostCardButton"); // follows Reduce motion live
        AutomationProperties.SetAutomationId(card, $"Host_{host.Name}");
        AutomationProperties.SetName(card, host.Compatible
            ? $"{host.Name}, {players} of {capacity} players"
            : $"{host.Name}, different CouchLink version");

        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9) };
        tile.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        var monitor = new Glyph();
        monitor.SetResourceReference(Glyph.DataProperty, "IconMonitor");
        tile.Child = monitor;
        DockPanel.SetDock(tile, Dock.Left);

        UIElement end;
        if (host.Compatible)
        {
            var chevron = new Glyph { Width = 16, Height = 16 };
            chevron.SetResourceReference(Glyph.DataProperty, "IconChevronRight");
            chevron.SetResourceReference(Glyph.BrushProperty, "PrimaryTextBrush");
            end = chevron;
        }
        else
        {
            end = new StatusPill { Text = "Different CouchLink version" };
        }
        DockPanel.SetDock(end, Dock.Right);

        var name = new TextBlock { Text = host.Name, FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (host.Compatible)
        {
            // Discovery sends a count, not slots: the dots show how full the game is, not who is in it.
            for (int slot = 1; slot <= Math.Min(players, 10); slot++)
                info.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = PlayerColors.BrushFor((byte)slot),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            var count = new TextBlock { Text = $"{players} / {capacity} players", FontSize = 12, Margin = new Thickness(4, 0, 0, 0) };
            count.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            info.Children.Add(count);
        }
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        if (host.Compatible)
            text.Children.Add(info);

        var row = new DockPanel();
        row.Children.Add(tile);
        row.Children.Add(end);
        row.Children.Add(text);
        card.Content = row;

        if (host.Compatible)
        {
            var chosen = host;
            card.Click += (_, _) => JoinRequested?.Invoke(chosen.Address, chosen.Name);
        }
        else
        {
            card.IsEnabled = false;
        }
        return card;
    }

    internal void JoinByAddress()
    {
        if (AddressInput.TryParse(AddressBox.Text, out var address, out var error))
        {
            AddressError.Visibility = Visibility.Collapsed;
            JoinRequested?.Invoke(address, address.ToString());
        }
        else
        {
            AddressError.Text = error;
            AddressError.Visibility = Visibility.Visible;
        }
    }

    public void Dispose()
    {
        _refresh.Stop();
        _noHosts.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _listener?.Dispose(); // frees UDP 47800 for the next list
        _listener = null;
    }
}
