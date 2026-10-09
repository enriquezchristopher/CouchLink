using System.Windows;
using CouchLink.App.Theme;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>Save as…: the profile's name (shown in every PC's list) and, optionally, the game it is for.</summary>
internal sealed partial class SaveProfileDialog : Window
{
    public SaveProfileDialog(string? name, string? game)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        NameBox.MaxLength = ProfileFile.MaxNameLength;
        GameBox.MaxLength = ProfileFile.MaxGameLength;
        NameBox.Text = name ?? "";
        GameBox.Text = game ?? "";
        SaveButton.IsEnabled = ProfileName.Length > 0;
        NameBox.TextChanged += (_, _) => SaveButton.IsEnabled = ProfileName.Length > 0;
        SaveButton.Click += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProfileName => NameBox.Text.Trim();

    public string? Game => string.IsNullOrWhiteSpace(GameBox.Text) ? null : GameBox.Text.Trim();
}
