using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchLink.App.Input;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private HostInputService? _host;
    private RawInputSource? _rawInput;
    private InputMapper? _mapper;
    private ClientInputLoop? _client;
    private ClientVideoService? _video;
    private string _clientStatus = "";

    public MainWindow()
    {
        InitializeComponent();
        for (int slot = 2; slot <= 10; slot++)
            SlotBox.Items.Add(slot);
        SlotBox.SelectedIndex = 0;
        foreach (var resolution in StreamSettings.Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = StreamSettings.Label(resolution), Tag = resolution });
        ResolutionBox.SelectedIndex = StreamSettings.Resolutions.ToList().IndexOf(StreamSettings.Default.Resolution);
        foreach (int rate in StreamSettings.FrameRatesFor(DisplayInfo.PrimaryRefreshRate()))
            FrameRateBox.Items.Add(new ComboBoxItem { Content = $"{rate} fps", Tag = rate });
        FrameRateBox.SelectedIndex = 0; // 60
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        Deactivated += (_, _) => _mapper?.ReleaseAll(); // never leave keys stuck
    }

    private void OnHost(object sender, RoutedEventArgs e)
    {
        var settings = new StreamSettings(
            (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
            (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag);
        if (!HostInputService.TryStart(settings, out _host, out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        HostButton.IsEnabled = JoinButton.IsEnabled = ResolutionBox.IsEnabled = FrameRateBox.IsEnabled = false;
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

        var slot = (int)SlotBox.SelectedItem;
        var inputSender = new InputSender(new IPEndPoint(ip, Ports.Input), (byte)slot);
        if (!ClientVideoService.TryStart(inputSender, AppServices.Options.SaveVideoPath, out _video, out var videoError))
        {
            inputSender.Dispose();
            MessageBox.Show(this, videoError, "CouchLink");
            return;
        }

        _mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(this));
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;

        _client = new ClientInputLoop(_mapper, inputSender);
        // Raw Input keys still reach focused controls; lock them so play can't change what's shown.
        HostButton.IsEnabled = JoinButton.IsEnabled = HostIpBox.IsEnabled = SlotBox.IsEnabled = false;
        ResolutionBox.IsEnabled = FrameRateBox.IsEnabled = false; // a client doesn't stream
        _clientStatus = $"Sending to {ip} as P{slot}";
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
        {
            StatusText.Text = $"Hosting. Virtual pads: {_host.PadCount}\n{_host.DescribeVideo()}" +
                (_host.LastError is { } err ? $"\nLast error: {err}" : "");
        }
        else if (_client is not null)
        {
            var s = _client.LastSent;
            StatusText.Text =
                $"{_clientStatus}\n" +
                $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}\n" +
                _video?.Describe();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _video?.Dispose();
        _client?.Dispose();
        _rawInput?.Dispose();
        _host?.Dispose();
        base.OnClosed(e);
    }
}
