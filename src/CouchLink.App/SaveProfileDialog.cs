using System.Windows;
using System.Windows.Controls;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>Save as…: the profile's name (shown in every PC's list) and, optionally, the game it is for.</summary>
internal sealed class SaveProfileDialog : Window
{
    private readonly TextBox _name = new() { MaxLength = ProfileFile.MaxNameLength };
    private readonly TextBox _game = new() { MaxLength = ProfileFile.MaxGameLength, Margin = new Thickness(0, 0, 0, 12) };

    public SaveProfileDialog(string? name, string? game)
    {
        Title = "Save profile";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _name.Text = name ?? "";
        _game.Text = game ?? "";

        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80, IsEnabled = ProfileName.Length > 0 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        _name.TextChanged += (_, _) => save.IsEnabled = ProfileName.Length > 0;
        save.Click += (_, _) => DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Name (shown in the profile list)", Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_name);
        root.Children.Add(new TextBlock { Text = "Game (optional)", Margin = new Thickness(0, 8, 0, 4) });
        root.Children.Add(_game);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public string ProfileName => _name.Text.Trim();

    public string? Game => string.IsNullOrWhiteSpace(_game.Text) ? null : _game.Text.Trim();
}
