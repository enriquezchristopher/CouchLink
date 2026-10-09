using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Theme;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App.Diagnostics;

/// <summary>
/// Tells the user where the crash report is and how to send it. Runs on its own thread with its own
/// copy of the theme; if the theme can't load it still opens, in the stock look.
/// </summary>
internal sealed partial class CrashDialog : Window
{
    /// <summary>Internal for tests; use <see cref="ShowAndWait"/>.</summary>
    internal CrashDialog(string heading, string? reportPath, string? saveError)
    {
        ThemeManager.TryInstallInto(this);
        InitializeComponent();
        WindowTheme.Apply(this);
        HeadingText.Text = heading;
        if (reportPath is not null)
        {
            PathBox.Text = reportPath;
        }
        else
        {
            SavedPanel.Visibility = Visibility.Collapsed;
            SaveErrorText.Text = $"The crash report could not be saved: {saveError}";
            SaveErrorText.Visibility = Visibility.Visible;
        }
        OpenFolderButton.IsEnabled = CopyPathButton.IsEnabled = reportPath is not null;
        OnClick(OpenFolderButton, () => Process.Start("explorer.exe", $"/select,\"{reportPath}\""));
        OnClick(CopyPathButton, () => Clipboard.SetText(reportPath!));
        OnClick(ReportButton, () => Process.Start(new ProcessStartInfo(ProjectLinks.NewIssue) { UseShellExecute = true }));
        OnClick(CloseButton, Close);
    }

    /// <summary>Shows the dialog modally on its own STA thread and waits. Never throws.</summary>
    public static void ShowAndWait(string heading, string? reportPath, string? saveError)
    {
        var thread = new Thread(() =>
        {
            try
            {
                new CrashDialog(heading, reportPath, saveError).ShowDialog();
            }
            catch
            {
                // Nothing left to tell the user with; the report file is already written.
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static void OnClick(Button button, Action action) => button.Click += (_, _) =>
    {
        try
        {
            action();
        }
        catch
        {
            // e.g. clipboard busy or no browser; the path is still on screen.
        }
    };
}
