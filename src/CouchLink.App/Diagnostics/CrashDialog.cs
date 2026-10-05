using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CouchLink.Core.Diagnostics;

namespace CouchLink.App.Diagnostics;

/// <summary>Tells the user where the crash report is and how to send it.</summary>
internal sealed class CrashDialog : Window
{
    private CrashDialog(string heading, string? reportPath, string? saveError)
    {
        Title = "CouchLink crashed";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        if (reportPath is not null)
        {
            panel.Children.Add(new TextBlock { Text = "A report was saved to:", Margin = new Thickness(0, 8, 0, 4) });
            var pathBox = new TextBox { Text = reportPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetAutomationId(pathBox, "ReportPath");
            panel.Children.Add(pathBox);
            panel.Children.Add(new TextBlock
            {
                Text = "Please attach this file to a new issue so we can fix it.",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            var error = new TextBlock
            {
                Text = $"The crash report could not be saved: {saveError}",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetAutomationId(error, "SaveError");
            panel.Children.Add(error);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(MakeButton("Open folder", "OpenFolderButton", reportPath is not null, () =>
            Process.Start("explorer.exe", $"/select,\"{reportPath}\"")));
        buttons.Children.Add(MakeButton("Copy path", "CopyPathButton", reportPath is not null, () =>
            Clipboard.SetText(reportPath!)));
        buttons.Children.Add(MakeButton("Report on GitHub", "ReportButton", true, () =>
            Process.Start(new ProcessStartInfo(ProjectLinks.NewIssue) { UseShellExecute = true })));
        buttons.Children.Add(MakeButton("Close", "CloseButton", true, Close));
        panel.Children.Add(buttons);

        Content = panel;
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

    private static Button MakeButton(string text, string automationId, bool enabled, Action onClick)
    {
        var button = new Button { Content = text, MinWidth = 110, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsEnabled = enabled };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            try
            {
                onClick();
            }
            catch
            {
                // e.g. clipboard busy or no browser; the path is still on screen.
            }
        };
        return button;
    }
}
