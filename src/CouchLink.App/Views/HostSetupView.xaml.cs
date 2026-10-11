using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Views;

/// <summary>
/// "Host a game": the controller every player gets (DualShock 4 or Xbox 360), then Start hosting. Nothing is
/// broadcast and no pads exist until then. Back and Esc return to the start screen.
/// </summary>
internal sealed partial class HostSetupView : UserControl
{
    public HostSetupView(PadKind initial)
    {
        InitializeComponent();
        foreach (var kind in new[] { PadKind.DualShock4, PadKind.Xbox360 })
        {
            var item = new RadioButton
            {
                Content = PadKinds.Label(kind),
                Tag = kind,
                GroupName = "PadKind",
                IsChecked = kind == initial,
            };
            item.SetResourceReference(StyleProperty, "SegmentedItem"); // follows Reduce motion live
            AutomationProperties.SetAutomationId(item, $"PadKind{kind}");
            item.Checked += (_, _) => HelpText.Text = HelpFor(Selected);
            PadKindGroup.Children.Add(item);
        }
        PadKindIndicator.Track(PadKindGroup);
        HelpText.Text = HelpFor(initial);
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        StartButton.Click += (_, _) => StartRequested?.Invoke(Selected);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            BackClicked?.Invoke();
        };
    }

    public event Action? BackClicked;
    public event Action<PadKind>? StartRequested;

    public PadKind Selected => (PadKind)PadKindGroup.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag;

    public static string HelpFor(PadKind kind) => kind == PadKind.Xbox360
        ? "Up to 4 players. For games that only work with Xbox controllers, like NBA 2K14."
        : "Up to 9 players. Works with most games, like NBA 2K22.";

    /// <summary>Hosting could not start: say why here and keep the choice.</summary>
    public void ShowError(string message)
    {
        ErrorBanner.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
