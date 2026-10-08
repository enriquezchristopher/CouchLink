using System.Windows.Controls;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>The main window while joining or playing: what is happening, and Cancel or Leave.</summary>
internal sealed partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        LeaveButton.Click += (_, _) => LeaveClicked?.Invoke();
    }

    public event Action? LeaveClicked;

    public string Details
    {
        set => DetailsText.Text = value;
    }

    public void Show(ClientState state, string host, byte slot)
    {
        (string heading, string hint, string button) = state switch
        {
            ClientState.Connecting => ($"Connecting to {host}...", "", "Cancel"),
            ClientState.Waiting => ($"Waiting for {host} to let you in...", "The host sees a popup and can allow or deny.", "Cancel"),
            ClientState.Playing => ($"Playing on {host} as P{slot}", "Ctrl+Alt+Q leaves. F2 shows stats.", "Leave"),
            ClientState.Reconnecting => ($"Reconnecting to {host}...", "Your slot is kept for a minute.", "Leave"),
            _ => (Heading.Text, Hint.Text, LeaveButton.Content as string ?? "Leave"),
        };
        Heading.Text = heading;
        Hint.Text = hint;
        LeaveButton.Content = button;
    }
}
