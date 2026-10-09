using System.Windows;
using CouchLink.App.Theme;

namespace CouchLink.App.Ui;

/// <summary>The app's message box: a title, a message, and OK or two choices. Replaces MessageBox.Show.</summary>
internal sealed partial class ThemedDialog : Window
{
    /// <summary>Internal for tests; use <see cref="Alert"/> or <see cref="Confirm"/>.</summary>
    internal ThemedDialog(Window? owner, string title, string message, string okText, string? cancelText, bool danger)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        WindowTheme.UseCustomChrome(this, 32);
        Motion.AnimateWindow(this, Body);
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        OkButton.Click += (_, _) => DialogResult = true;
        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            OkButton.IsDefault = true;
        }
        else
        {
            CancelButton.Content = cancelText;
            // A destructive choice is never the default: a stray Enter keeps things as they are.
            OkButton.IsDefault = !danger;
            CancelButton.IsDefault = danger;
        }
        if (danger)
            OkButton.SetResourceReference(StyleProperty, "DangerFilledButton");
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            Topmost = owner.Topmost;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Loaded += (_, _) => (CancelButton.IsDefault ? CancelButton : OkButton).Focus();
    }

    public static void Alert(Window? owner, string title, string message) =>
        new ThemedDialog(owner, title, message, "OK", null, danger: false).ShowDialog();

    public static bool Confirm(Window? owner, string title, string message, string confirmText, string cancelText, bool danger) =>
        new ThemedDialog(owner, title, message, confirmText, cancelText, danger).ShowDialog() == true;
}
