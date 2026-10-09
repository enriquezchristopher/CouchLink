using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;
using Microsoft.Win32;

namespace CouchLink.App;

/// <summary>
/// Controls: every DS4 control in grouped cards with its keys. Click a row, then press a key or mouse
/// button to bind it (Esc cancels); when the key came from another control, that row lights up and the
/// banner says so. A profile bar loads a profile from the profiles folder or a file and saves the
/// current controls as one; Find filters the rows; Labels adds a box per row for the game's name of
/// each button. Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
internal sealed partial class ControlsWindow : Window
{
    private const string ListeningText = "Press a key or mouse button · Esc cancels";
    private const string FileFilter = "CouchLink profile (*.json)|*.json";
    private const double NarrowWidth = 460, WideWidth = 600;
    private static ControlsWindow? _open;

    /// <summary>The profile file loaded last this run. <see cref="ControlSettings.ProfileName"/> says whether it still is.</summary>
    private static ProfileChoice? _loaded;

    private readonly ProfileStore _store = new(ProfileStore.DefaultFolder, AppServices.Log.Write);
    private readonly ControlSettings _settings = AppServices.Controls;
    private readonly Dictionary<PadControl, Button> _rows = [];
    private readonly Dictionary<PadControl, DockPanel> _lines = [];
    private readonly Dictionary<PadControl, TextBox> _labelBoxes = [];
    private readonly Dictionary<PadControl, string> _groupOf = [];
    private readonly List<(string Name, TextBlock Heading, Border Card, PadControl[] Controls)> _groups = [];
    private readonly DispatcherTimer _messageTimer = new();
    private readonly DispatcherTimer _movedTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private IReadOnlyList<ProfileEntry> _entries = [];
    private bool _updatingProfiles;

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

    /// <summary>Internal for tests; use <see cref="Open"/>.</summary>
    internal ControlsWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        WindowTheme.UseCustomChrome(this, 32);
        Motion.AnimateWindow(this, Body);
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        BuildRows();
        Loaded += (_, _) =>
        {
            // The group cards (with their headings) come in one after another as the window opens.
            for (int i = 0; i < _groups.Count; i++)
            {
                Motion.RiseIn(_groups[i].Heading, i);
                Motion.RiseIn(_groups[i].Card, i);
            }
        };

        SensitivitySlider.Minimum = ControlSettings.MinStep;
        SensitivitySlider.Maximum = ControlSettings.MaxStep;
        SensitivitySlider.ValueChanged += (_, e) => _settings.SetSensitivityStep((int)Math.Round(e.NewValue));
        InvertYToggle.Click += (_, _) => _settings.SetInvertY(InvertYToggle.IsChecked == true);
        ResetButton.Click += (_, _) =>
        {
            Listening = null;
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.", BannerKind.Info);
        };
        DoneButton.Click += (_, _) => Close();
        BrowseButton.Click += (_, _) => Browse();
        SaveAsButton.Click += (_, _) => SaveAs();
        ProfileBox.SelectionChanged += (_, _) => OnProfileChosen();
        // Checked/Unchecked, not Click: screen readers and other accessibility tools tick it without a click
        LabelsToggle.Checked += (_, _) => ShowLabels(true);
        LabelsToggle.Unchecked += (_, _) => ShowLabels(false);
        FindBox.TextChanged += (_, _) => ApplyFilter();
        FindBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && FindBox.Text.Length > 0)
            {
                FindBox.Text = "";
                e.Handled = true;
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageBanner.Visibility = Visibility.Collapsed;
        };
        _movedTimer.Tick += (_, _) =>
        {
            _movedTimer.Stop();
            Moved = null;
            Refresh();
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
            _movedTimer.Stop();
        };
        _entries = _store.List();
        Refresh();
    }

    /// <summary>The row waiting for a key, or null.</summary>
    internal PadControl? Listening { get; private set; }

    /// <summary>The row that just lost its key to another one; tinted for 4 s.</summary>
    internal PadControl? Moved { get; private set; }

    internal bool IsRowVisible(PadControl control) => _lines[control].Visibility == Visibility.Visible;

    internal bool IsGroupVisible(string group) => _groups.First(g => g.Name == group).Card.Visibility == Visibility.Visible;

    private void BuildRows()
    {
        foreach (var (group, controls) in KeyNames.Groups)
        {
            var heading = new TextBlock { Text = group.ToUpperInvariant(), Style = (Style)FindResource("OverlineText"), Margin = new Thickness(4, 12, 0, 6) };
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(4) };
            var rows = new StackPanel();
            foreach (var control in controls)
            {
                var c = control;
                var row = new Button();
                row.SetResourceReference(StyleProperty, "ControlRowButton"); // follows Reduce motion live
                AutomationProperties.SetAutomationId(row, $"Row_{control}");
                AutomationProperties.SetName(row, KeyNames.Of(control));
                row.Click += (_, _) => Listen(c);
                _rows[control] = row;

                var label = new TextBox
                {
                    MaxLength = ProfileFile.MaxLabelLength,
                    Width = 140,
                    Margin = new Thickness(6, 2, 2, 2),
                    Visibility = Visibility.Collapsed,
                    ToolTip = "What this button does in the game, e.g. Shoot",
                };
                ThemeProps.SetPlaceholder(label, "Label");
                AutomationProperties.SetName(label, $"Label for {KeyNames.Of(control)}");
                AutomationProperties.SetAutomationId(label, $"Label_{control}");
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
                _lines[control] = line;
                _groupOf[control] = group;
                rows.Children.Add(line);
            }
            card.Child = rows;
            GroupList.Children.Add(heading);
            GroupList.Children.Add(card);
            _groups.Add((group, heading, card, controls));
        }
    }

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        foreach (var (control, row) in _rows)
        {
            row.Content = RowContent(control);
            PaintRow(control, row);
        }
        foreach (var (control, box) in _labelBoxes)
            if (!box.IsKeyboardFocusWithin) // don't overwrite what is being typed
                box.Text = _settings.LabelFor(control) ?? "";
        SensitivitySlider.Value = _settings.SensitivityStep;
        SensitivityValue.Text = _settings.SensitivityStep.ToString();
        InvertYToggle.IsChecked = _settings.InvertY;
        RefreshProfiles();
        ApplyFilter();
    }

    private DockPanel RowContent(PadControl control)
    {
        var panel = new DockPanel();
        if (Listening == control)
        {
            var hint = new TextBlock { Text = ListeningText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            DockPanel.SetDock(hint, Dock.Right);
            panel.Children.Add(hint);
        }
        else
        {
            var keys = new StackPanel { Orientation = Orientation.Horizontal };
            var bound = _settings.Layout.KeysFor(control);
            if (bound.Count == 0)
                keys.Children.Add(new KeyCap());
            foreach (var key in bound)
                keys.Children.Add(new KeyCap { Key = KeyNames.Of(key) });
            DockPanel.SetDock(keys, Dock.Right);
            panel.Children.Add(keys);
        }
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new Run(ControlFilter.RowName(control)));
        if (LabelsToggle.IsChecked != true && _settings.LabelFor(control) is { } label)
        {
            var run = new Run($"  ·  {label}");
            run.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
            name.Inlines.Add(run);
        }
        panel.Children.Add(name);
        return panel;
    }

    private void PaintRow(PadControl control, Button row)
    {
        if (Listening == control)
        {
            row.SetResourceReference(BackgroundProperty, "ListeningFillBrush");
            row.SetResourceReference(BorderBrushProperty, "PrimaryTextBrush");
        }
        else if (Moved == control)
        {
            row.SetResourceReference(BackgroundProperty, "WarningBannerFillBrush");
            row.SetResourceReference(BorderBrushProperty, "WarningBannerBorderBrush");
        }
        else
        {
            row.ClearValue(BackgroundProperty);
            row.ClearValue(BorderBrushProperty);
        }
    }

    private void ApplyFilter()
    {
        string query = FindBox.Text;
        foreach (var (name, heading, card, controls) in _groups)
        {
            bool any = false;
            foreach (var control in controls)
            {
                bool match = ControlFilter.Matches(control, name, _settings.LabelFor(control), query);
                _lines[control].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }
            heading.Visibility = card.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
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
            ProfileBox.ItemsSource = choices;
            ProfileBox.SelectedIndex = selected;
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
        if (_updatingProfiles || ProfileBox.SelectedItem is not ProfileChoice choice)
            return;
        Listening = null;
        if (choice.Path is null)
        {
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.", BannerKind.Info);
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
        ShowMessage($"Loaded {profile.Name}.", BannerKind.Info);
    }

    private void Browse()
    {
        Listening = null;
        Refresh();
        var dialog = new OpenFileDialog { Title = "Load a controller profile", Filter = FileFilter };
        if (Directory.Exists(_store.Folder))
            dialog.InitialDirectory = _store.Folder;
        if (dialog.ShowDialog(this) == true)
            Load(dialog.FileName);
    }

    private void SaveAs()
    {
        Listening = null;
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
        ShowMessage($"Saved {Path.GetFileName(dialog.FileName)}.", BannerKind.Info);
    }

    private void ShowLabels(bool show)
    {
        foreach (var box in _labelBoxes.Values)
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Width = show ? WideWidth : NarrowWidth;
        Refresh(); // the name column drops or shows the label text
    }

    private void CommitLabel(PadControl control) => _settings.SetLabel(control, _labelBoxes[control].Text);

    internal void Listen(PadControl control)
    {
        Listening = control;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Listening is not { } control)
            return;
        e.Handled = true; // Space, Enter and Tab must not press a button here
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Listening = null;
            Refresh();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and <= 0xFF)
            Bind(control, (ushort)vk);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Listening is not { } control)
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

    internal void Bind(PadControl control, ushort key)
    {
        var result = _settings.Bind(control, key);
        if (!result.Bound)
        {
            ShowMessage(BindMessage.Reserved(key), BannerKind.Warning);
            return; // keep listening
        }
        Listening = null;
        Moved = result.MovedFrom;
        if (result.MovedFrom is { } from)
        {
            int left = _settings.Layout.KeysFor(from).Count;
            ShowMessage(BindMessage.Moved(key, from, left)!, BannerKind.Warning, seconds: 6);
            _movedTimer.Stop();
            _movedTimer.Start();
        }
        Refresh();
        if (result.MovedFrom is { } moved)
            _rows[moved].BringIntoView();
    }

    private void ShowMessage(string text, BannerKind kind, double seconds = 3)
    {
        MessageBanner.Kind = kind;
        MessageBanner.Text = text;
        MessageBanner.Visibility = Visibility.Visible;
        _messageTimer.Stop();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.Start();
    }

    /// <summary>Errors stay long enough to read a file problem such as "Square": unknown key "Spcae".</summary>
    private void ShowError(string text) => ShowMessage(text, BannerKind.Error, 10);
}
