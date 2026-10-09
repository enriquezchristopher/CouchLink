using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CouchLink.App.Ui;

/// <summary>
/// Minimize, maximize/restore and close for a window that draws its own title bar. Minimize and maximize
/// follow the window's ResizeMode (a fixed window shows only close). Never focusable: they are for the mouse,
/// and Alt+Space and the system menu cover the keyboard.
/// </summary>
internal sealed partial class CaptionButtons : UserControl
{
    private Window? _window;

    public CaptionButtons()
    {
        InitializeComponent();
        MinimizeButton.Click += (_, _) => Act(SystemCommands.MinimizeWindow);
        MaximizeButton.Click += (_, _) =>
            Act(w => { if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w); else SystemCommands.MaximizeWindow(w); });
        CloseButton.Click += (_, _) => Act(SystemCommands.CloseWindow);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Act(Action<Window> action)
    {
        if (FindWindow() is { } window)
            action(window);
    }

    private Window? FindWindow()
    {
        DependencyObject? node = this;
        while (node is not null and not Window)
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        return node as Window;
    }

    private void Attach()
    {
        Detach();
        _window = FindWindow();
        if (_window is null)
            return;
        _window.StateChanged += OnStateChanged;
        var mode = _window.ResizeMode;
        MinimizeButton.Visibility = mode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
        MaximizeButton.Visibility = mode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip ? Visibility.Visible : Visibility.Collapsed;
        ShowState(_window.WindowState);
    }

    private void Detach()
    {
        if (_window is not null)
            _window.StateChanged -= OnStateChanged;
        _window = null;
    }

    private void OnStateChanged(object? sender, EventArgs e) => ShowState(((Window)sender!).WindowState);

    private void ShowState(WindowState state)
    {
        bool maximized = state == WindowState.Maximized;
        MaximizeButton.Content = FindResource(maximized ? "CaptionRestoreGlyph" : "CaptionMaximizeGlyph");
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }
}
