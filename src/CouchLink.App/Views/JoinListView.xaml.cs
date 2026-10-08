using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.Core.Net;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>
/// The hosts on the LAN, "PC-03 · 4/10 players", heard on UDP 47800 while this view is shown, plus
/// "Join by address..." for when broadcasts don't get through. A message bar says why the last
/// session ended.
/// </summary>
internal sealed partial class JoinListView : UserControl, IDisposable
{
    private readonly HostList _hosts = new(TimeProvider.System);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DiscoveryListener? _listener;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<FoundHost> _shown = [];

    public JoinListView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        ByAddressLink.Click += (_, _) =>
        {
            ByAddressPanel.Visibility = Visibility.Visible;
            AddressBox.Focus();
        };
        AddressJoinButton.Click += (_, _) => JoinByAddress();
        AddressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                JoinByAddress();
        };
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => StartListening();
        Unloaded += (_, _) => Dispose();
    }

    public event Action? BackClicked;
    public event Action<IPAddress, string>? JoinRequested;

    public void ShowMessage(string? message)
    {
        MessageText.Text = message ?? "";
        MessageBar.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartListening()
    {
        if (_listener is not null)
            return;
        if (!DiscoveryListener.TryCreate(Ports.Discovery, out _listener, out var error))
        {
            EmptyText.Text = $"{error} Join by address still works.";
            ByAddressPanel.Visibility = Visibility.Visible;
            return;
        }
        _cts = new CancellationTokenSource();
        _ = _listener!.RunAsync((announce, from) => _hosts.Seen(announce, from), _cts.Token,
            e => AppServices.Log.Write($"Discovery error: {e}"));
        _refresh.Start();
    }

    private void Refresh()
    {
        var hosts = _hosts.Current();
        if (hosts.SequenceEqual(_shown))
            return;
        _shown = hosts;
        EmptyText.Visibility = hosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HostButtons.Children.Clear();
        foreach (var host in hosts)
        {
            var button = new Button
            {
                Height = 56,
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            if (host.Compatible)
            {
                button.Content = $"{host.Name} · {host.Players + 1}/{host.Capacity + 1} players";
                var chosen = host;
                button.Click += (_, _) => JoinRequested?.Invoke(chosen.Address, chosen.Name);
            }
            else
            {
                button.Content = $"{host.Name} · needs the same CouchLink version";
                button.IsEnabled = false;
            }
            HostButtons.Children.Add(button);
        }
    }

    private void JoinByAddress()
    {
        if (IPAddress.TryParse(AddressBox.Text.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetwork)
            JoinRequested?.Invoke(address, address.ToString());
        else
            ShowMessage("Enter an IP address like 192.168.1.23.");
    }

    public void Dispose()
    {
        _refresh.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _listener?.Dispose(); // frees UDP 47800 for the next list
        _listener = null;
    }
}
