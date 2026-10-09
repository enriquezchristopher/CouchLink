using System.Windows;
using CouchLink.App.Diagnostics;
using CouchLink.App.Theme;

namespace CouchLink.App.Tests;

public class PopupAndDialogTests
{
    [Fact]
    public void The_toast_names_the_pc_and_counts_down()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var toast = new ApprovalPopup("PC-11", 0, () => { }, () => { });
            Assert.Equal("PC-11 wants to join", toast.Heading.Text);
            Assert.False(toast.ShowActivated);
            Assert.True(toast.Topmost);
            toast.UpdateRemaining(TimeSpan.FromSeconds(8));
            Assert.Equal("Denied automatically in 22 s", toast.Remaining.Text);
            toast.CloseByHost();
        });
    }

    [Fact]
    public void Save_is_disabled_until_the_profile_has_a_name()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var dialog = new SaveProfileDialog(null, null);
            Assert.False(dialog.SaveButton.IsEnabled);
            dialog.NameBox.Text = "  NBA 2K22 (my keys) ";
            Assert.True(dialog.SaveButton.IsEnabled);
            Assert.Equal("NBA 2K22 (my keys)", dialog.ProfileName);
            Assert.Null(dialog.Game);
            dialog.Close();
        });
    }

    [Fact]
    public void The_crash_dialog_shows_the_path_or_the_save_error()
    {
        Wpf.Run(() =>
        {
            var saved = new CrashDialog("CouchLink crashed.", @"C:\reports\crash.txt", null);
            Assert.Equal(@"C:\reports\crash.txt", saved.PathBox.Text);
            Assert.Equal(Visibility.Visible, saved.SavedPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, saved.SaveErrorText.Visibility);
            Assert.True(saved.OpenFolderButton.IsEnabled);
            saved.Close();

            var failed = new CrashDialog("CouchLink crashed.", null, "disk full");
            Assert.Equal(Visibility.Collapsed, failed.SavedPanel.Visibility);
            Assert.Equal("The crash report could not be saved: disk full", failed.SaveErrorText.Text);
            Assert.False(failed.OpenFolderButton.IsEnabled);
            Assert.False(failed.CopyPathButton.IsEnabled);
            failed.Close();
        });
    }
}
