using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>
/// ⚙ Controls: every DS4 control with its keys. Click a row, then press a key or mouse button to bind
/// it (Esc cancels). Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
internal sealed class ControlsWindow : Window
{
    private const string Listening = "Press a key or mouse button... (Esc cancels)";
    private static ControlsWindow? _open;

    private readonly ControlSettings _settings = AppServices.Controls;
    private readonly Dictionary<PadControl, Button> _rows = [];
    private readonly TextBlock _message = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Slider _sensitivity;
    private readonly CheckBox _invertY;
    private PadControl? _listening;

    /// <summary>Shows the editor, or brings the open one forward. <paramref name="overGame"/>: opened with Ctrl+Alt+C over the fullscreen player.</summary>
    public static void Open(Window? owner, bool overGame = false, Action? closed = null)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new ControlsWindow { Topmost = overGame };
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized } && !overGame)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        window.Closed += (_, _) =>
        {
            _open = null;
            closed?.Invoke();
        };
        _open = window;
        window.Show();
        window.Activate();
    }

    private ControlsWindow()
    {
        Title = "Controls";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var list = new StackPanel();
        foreach (var (group, controls) in KeyNames.Groups)
        {
            list.Children.Add(new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var control in controls)
            {
                var row = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 1, 0, 1),
                };
                var c = control;
                row.Click += (_, _) => Listen(c);
                _rows[control] = row;
                list.Children.Add(row);
            }
        }
        list.Children.Add(new TextBlock { Text = "Right stick", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        list.Children.Add(new Button
        {
            Content = Row("Right stick", "Mouse"),
            IsEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 4, 8, 4),
        });

        _sensitivity = new Slider
        {
            Minimum = ControlSettings.MinStep,
            Maximum = ControlSettings.MaxStep,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            TickPlacement = TickPlacement.BottomRight,
            Value = _settings.SensitivityStep,
        };
        _sensitivity.ValueChanged += (_, e) => _settings.SetSensitivityStep((int)Math.Round(e.NewValue));
        _invertY = new CheckBox { Content = "Invert Y (mouse toward you pushes the stick up)", Margin = new Thickness(0, 8, 0, 0) };
        _invertY.Click += (_, _) => _settings.SetInvertY(_invertY.IsChecked == true);
        var reset = new Button { Content = "Reset to default", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            _listening = null;
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.");
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Click a control, then press the key or mouse button for it.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(_message);
        root.Children.Add(new TextBlock { Text = "Mouse sensitivity (right stick)", Margin = new Thickness(0, 12, 0, 4) });
        root.Children.Add(_sensitivity);
        root.Children.Add(_invertY);
        root.Children.Add(reset);
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _message.Text = "";
        };
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _messageTimer.Stop();
        };
        Refresh();
    }

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        foreach (var (control, row) in _rows)
            row.Content = Row(KeyNames.Of(control), _listening == control ? Listening : KeyNames.Describe(_settings.Layout, control));
        _sensitivity.Value = _settings.SensitivityStep;
        _invertY.IsChecked = _settings.InvertY;
    }

    private static DockPanel Row(string name, string keys)
    {
        var panel = new DockPanel();
        var keysText = new TextBlock { Text = keys, Foreground = Brushes.DimGray };
        DockPanel.SetDock(keysText, Dock.Right);
        panel.Children.Add(keysText);
        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    private void Listen(PadControl control)
    {
        _listening = control;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listening is not { } control)
            return;
        e.Handled = true; // Space, Enter and Tab must not press a button here
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _listening = null;
            Refresh();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and <= 0xFF)
            Bind(control, (ushort)vk);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_listening is not { } control)
            return;
        e.Handled = true;
        Bind(control, e.ChangedButton switch
        {
            MouseButton.Left => VirtualKeys.LButton,
            MouseButton.Right => VirtualKeys.RButton,
            MouseButton.Middle => VirtualKeys.MButton,
            MouseButton.XButton1 => VirtualKeys.XButton1,
            _ => VirtualKeys.XButton2,
        });
    }

    private void Bind(PadControl control, ushort key)
    {
        var result = _settings.Bind(control, key);
        if (!result.Bound)
        {
            ShowMessage($"{KeyNames.Of(key)} is reserved. Press another key.");
            return; // keep listening
        }
        _listening = null;
        Refresh();
        if (result.MovedFrom is { } from)
            ShowMessage($"{KeyNames.Of(key)} moved from {KeyNames.Of(from)}.");
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _messageTimer.Stop();
        _messageTimer.Start();
    }
}
