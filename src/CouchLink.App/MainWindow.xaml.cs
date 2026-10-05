using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchLink.App.Input;
using CouchLink.Core.Input;
using CouchLink.Core.Net;

namespace CouchLink.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private HostInputService? _host;
    private RawInputSource? _rawInput;
    private InputMapper? _mapper;
    private ClientInputLoop? _client;

    public MainWindow()
    {
        InitializeComponent();
        for (int slot = 2; slot <= 10; slot++)
            SlotBox.Items.Add(slot);
        SlotBox.SelectedIndex = 0;
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        Deactivated += (_, _) => _mapper?.ReleaseAll(); // never leave keys stuck
    }

    private void OnHost(object sender, RoutedEventArgs e)
    {
        if (!HostInputService.TryStart(out _host, out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        HostButton.IsEnabled = JoinButton.IsEnabled = false;
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(HostIpBox.Text.Trim(), out var ip))
        {
            MessageBox.Show(this, "Enter a valid host IP address.", "CouchLink");
            return;
        }

        _mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(this));
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;

        var inputSender = new InputSender(new IPEndPoint(ip, Ports.Input), (byte)(int)SlotBox.SelectedItem);
        _client = new ClientInputLoop(_mapper, inputSender);
        HostButton.IsEnabled = JoinButton.IsEnabled = false;
        var slot = (int)SlotBox.SelectedItem;
        AppServices.DescribeMode = () => $"Client (slot P{slot})";
        AppServices.Log.Write($"Joined as P{slot}");
    }

    private void OnOpenCrashReports(object sender, RoutedEventArgs e)
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

    private void UpdateStatus()
    {
        if (_host is not null)
            StatusText.Text = $"Hosting. Virtual pads: {_host.PadCount}" +
                (_host.LastError is { } err ? $"\nPad error: {err}" : "");
        else if (_client is not null)
        {
            var s = _client.LastSent;
            StatusText.Text =
                $"Sending to {HostIpBox.Text} as P{SlotBox.SelectedItem}\n" +
                $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _client?.Dispose();
        _rawInput?.Dispose();
        _host?.Dispose();
        base.OnClosed(e);
    }
}
