using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CouchLink.App.Presentation;

namespace CouchLink.App.Ui;

/// <summary>
/// The bar on every main-window screen: logo, Controls, and a Help menu with Reduce motion (a per-PC
/// setting that applies at once), Crash reports and About.
/// </summary>
internal sealed partial class AppHeader : UserControl
{
    public AppHeader()
    {
        InitializeComponent();
        ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();
        HelpButton.Click += (_, _) =>
        {
            HelpMenu.PlacementTarget = HelpButton;
            HelpMenu.Placement = PlacementMode.Bottom;
            HelpMenu.IsOpen = true;
        };
        ReduceMotionItem.SetBinding(MenuItem.IsCheckedProperty, new Binding(nameof(MotionSettings.ReduceMotion))
        {
            Source = AppServices.MotionSettings,
            Mode = BindingMode.TwoWay,
        });
        CrashReportsItem.Click += (_, _) => CrashReportsClicked?.Invoke();
        AboutItem.Click += (_, _) => AboutClicked?.Invoke();
    }

    public event Action? ControlsClicked;
    public event Action? CrashReportsClicked;
    public event Action? AboutClicked;

    /// <summary>False while playing: Raw Input keys still reach a focused button, so Space in the game would press it.</summary>
    public bool ButtonsFocusable
    {
        set
        {
            ControlsButton.Focusable = value;
            HelpButton.Focusable = value;
        }
    }
}
