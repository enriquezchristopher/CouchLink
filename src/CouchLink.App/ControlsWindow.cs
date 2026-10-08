using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Input;
using Microsoft.Win32;

namespace CouchLink.App;

/// <summary>
/// ⚙ Controls: every DS4 control with its keys. Click a row, then press a key or mouse button to bind
/// it (Esc cancels). A profile row on top loads a profile from the profiles folder or a file and saves
/// the current controls as one; Show labels adds a box per row for the game's name of each button.
/// Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
internal sealed class ControlsWindow : Window
{
    private const string Listening = "Press a key or mouse button... (Esc cancels)";
    private const string FileFilter = "CouchLink profile (*.json)|*.json";
    private const double NarrowWidth = 440, WideWidth = 600;
    private static ControlsWindow? _open;

    /// <summary>The profile file loaded last this run. <see cref="ControlSettings.ProfileName"/> says whether it still is.</summary>
    private static ProfileChoice? _loaded;

    private readonly ProfileStore _store = new(ProfileStore.DefaultFolder, AppServices.Log.Write);
    private readonly ComboBox _profile = new() { MinWidth = 180 };
    private readonly CheckBox _showLabels = new() { Content = "Show labels (what each button does in the game)", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<PadControl, TextBox> _labelBoxes = [];
    private IReadOnlyList<ProfileEntry> _entries = [];
    private bool _updatingProfiles;

    private readonly ControlSettings _settings = AppServices.Controls;
    private readonly Dictionary<PadControl, Button> _rows = [];
    private readonly TextBlock _message = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Slider _sensitivity;
    private readonly CheckBox _invertY;
    private PadControl? _listening;

    private sealed record ProfileChoice(string Text, string? Path)
    {
        public static readonly ProfileChoice Default = new("Default layout", null);
        public override string ToString() => Text;
    }

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
        Width = NarrowWidth;
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

                var label = new TextBox
                {
                    MaxLength = ProfileFile.MaxLabelLength,
                    Width = 140,
                    Margin = new Thickness(6, 1, 0, 1),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    ToolTip = "What this button does in the game, e.g. Shoot",
                };
                label.LostFocus += (_, _) => CommitLabel(c);
                label.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    CommitLabel(c);
                    e.Handled = true;
                };
                _labelBoxes[control] = label;

                var line = new DockPanel();
                DockPanel.SetDock(label, Dock.Right);
                line.Children.Add(label);
                line.Children.Add(row);
                list.Children.Add(line);
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

        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var saveAs = new Button { Content = "Save as…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        saveAs.Click += (_, _) => SaveAs();
        _profile.SelectionChanged += (_, _) => OnProfileChosen();
        _showLabels.Click += (_, _) => ShowLabels(_showLabels.IsChecked == true);

        var profileLabel = new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var profileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(profileLabel, Dock.Left);
        DockPanel.SetDock(saveAs, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        profileRow.Children.Add(profileLabel);
        profileRow.Children.Add(saveAs);
        profileRow.Children.Add(browse);
        profileRow.Children.Add(_profile);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(profileRow);
        root.Children.Add(new TextBlock { Text = "Click a control, then press the key or mouse button for it.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_showLabels);
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
        Closing += (_, _) =>
        {
            foreach (var control in _labelBoxes.Keys)
                CommitLabel(control); // a label typed just before closing is kept
        };
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _messageTimer.Stop();
        };
        _entries = _store.List();
        Refresh();
    }

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        foreach (var (control, row) in _rows)
        {
            string name = KeyNames.Labelled(control, _settings.LabelFor(control));
            row.Content = Row(name, _listening == control ? Listening : KeyNames.Describe(_settings.Layout, control));
        }
        foreach (var (control, box) in _labelBoxes)
            if (!box.IsKeyboardFocusWithin) // don't overwrite what is being typed
                box.Text = _settings.LabelFor(control) ?? "";
        _sensitivity.Value = _settings.SensitivityStep;
        _invertY.IsChecked = _settings.InvertY;
        RefreshProfiles();
    }

    /// <summary>Rebuilds the dropdown: Default layout, the folder's profiles, and the loaded file if it is elsewhere.</summary>
    private void RefreshProfiles()
    {
        var choices = new List<ProfileChoice> { ProfileChoice.Default };
        choices.AddRange(_entries.Select(e => new ProfileChoice(e.DisplayName, e.Path)));
        int selected = 0;
        if (_settings.ProfileName is not null && _loaded is { Path: { } loadedPath } loaded)
        {
            selected = choices.FindIndex(c => c.Path is not null && SamePath(c.Path, loadedPath));
            if (selected < 0)
            {
                choices.Add(loaded);
                selected = choices.Count - 1;
            }
            if (_settings.ProfileChanged)
                choices[selected] = choices[selected] with { Text = choices[selected].Text + " (changed)" };
        }
        _updatingProfiles = true;
        try
        {
            _profile.ItemsSource = choices;
            _profile.SelectedIndex = selected;
        }
        finally
        {
            _updatingProfiles = false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void OnProfileChosen()
    {
        if (_updatingProfiles || _profile.SelectedItem is not ProfileChoice choice)
            return;
        _listening = null;
        if (choice.Path is null)
        {
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.");
            return;
        }
        Load(choice.Path);
    }

    /// <summary>Reads the file again (it may have changed since the list was made) and applies it, or says why not.</summary>
    private void Load(string path)
    {
        var result = ProfileStore.Read(path);
        if (result.Profile is not { } profile)
        {
            AppServices.Log.Write($"profile not loaded: {path}: {result.Error}");
            ShowError(result.Error!);
            RefreshProfiles(); // the dropdown goes back to what is really loaded
            return;
        }
        _loaded = new ProfileChoice(profile.Name, path);
        _settings.Apply(profile);
        ShowMessage($"Loaded {profile.Name}.");
    }

    private void Browse()
    {
        _listening = null;
        Refresh();
        var dialog = new OpenFileDialog { Title = "Load a controller profile", Filter = FileFilter };
        if (Directory.Exists(_store.Folder))
            dialog.InitialDirectory = _store.Folder;
        if (dialog.ShowDialog(this) == true)
            Load(dialog.FileName);
    }

    private void SaveAs()
    {
        _listening = null;
        Refresh();
        var details = new SaveProfileDialog(_settings.ProfileName, _settings.ProfileGame) { Owner = this, Topmost = Topmost };
        if (details.ShowDialog() != true)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save controller profile",
            Filter = FileFilter,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = ProfileStore.SuggestedFileName(details.ProfileName),
            InitialDirectory = _store.TryCreateFolder()
                ? _store.Folder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var profile = _settings.ToProfile(details.ProfileName, details.Game);
        if (_store.Write(dialog.FileName, profile) is { } error)
        {
            ShowError(error);
            return;
        }
        _entries = _store.List();
        _loaded = new ProfileChoice(profile.Name, dialog.FileName);
        _settings.Apply(profile); // the saved file is now the loaded profile, unchanged
        ShowMessage($"Saved {Path.GetFileName(dialog.FileName)}.");
    }

    private void ShowLabels(bool show)
    {
        foreach (var box in _labelBoxes.Values)
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Width = show ? WideWidth : NarrowWidth;
    }

    private void CommitLabel(PadControl control) => _settings.SetLabel(control, _labelBoxes[control].Text);

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

    private void ShowMessage(string text, double seconds = 3)
    {
        _message.Text = text;
        _messageTimer.Stop();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.Start();
    }

    /// <summary>Errors stay long enough to read a file problem such as "Square": unknown key "Spcae".</summary>
    private void ShowError(string text) => ShowMessage(text, 10);
}
