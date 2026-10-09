using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Presentation;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>The main window while joining or playing: the steps, what is happening, the shortcuts, and Cancel or Leave.</summary>
internal sealed partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        LeaveButton.Click += (_, _) => LeaveClicked?.Invoke();
        ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();
    }

    public event Action? LeaveClicked;
    public event Action? ControlsClicked;

    public string Details
    {
        set => DetailsText.Text = value;
    }

    public void Show(ClientState state, string host, byte slot)
    {
        if (SessionText.For(state, host, slot) is not { } text)
            return;
        Steps.Current = text.Step;
        Heading.Text = text.Heading;
        Hint.Text = text.Hint;
        Hint.Visibility = text.Hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LeaveButton.Content = text.LeaveText;
        Chip.Slot = slot;
        Chip.Visibility = text.Playing ? Visibility.Visible : Visibility.Collapsed;
        Busy.Visibility = text.Playing ? Visibility.Collapsed : Visibility.Visible;
        Shortcuts.Visibility = text.Playing ? Visibility.Visible : Visibility.Collapsed;
        ReconnectingPill.Visibility = text.Reconnecting ? Visibility.Visible : Visibility.Collapsed;
    }
}
