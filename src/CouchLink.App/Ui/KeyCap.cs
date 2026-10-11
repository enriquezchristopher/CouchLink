using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>A key name drawn like a key cap, or a dashed "No key" when the control has none.</summary>
internal sealed class KeyCap : Grid
{
    private readonly Border _cap = new() { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 3) };
    private readonly Rectangle _dashed = new() { RadiusX = 6, RadiusY = 6, StrokeThickness = 1, StrokeDashArray = [3, 2] };
    private readonly TextBlock _text = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 2, 8, 2) };
    private string? _key;

    public KeyCap()
    {
        MinHeight = 24;
        Margin = new Thickness(4, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        _cap.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        _cap.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        _dashed.SetResourceReference(Shape.StrokeProperty, "BorderStrongBrush");
        _text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        Children.Add(_cap);
        Children.Add(_dashed);
        Children.Add(_text);
        Key = null;
    }

    public string Text => _text.Text;

    /// <summary>The key this cap shows, so the editor can light it while the key is held.</summary>
    public ushort? VirtualKey { get; init; }

    private bool _lit;

    /// <summary>Held right now: the cap fills with the primary colour.</summary>
    public bool IsLit
    {
        get => _lit;
        set
        {
            if (value == _lit || string.IsNullOrEmpty(_key))
                return;
            _lit = value;
            _cap.SetResourceReference(Border.BackgroundProperty, value ? "PrimaryBrush" : "RaisedBrush");
            _cap.SetResourceReference(Border.BorderBrushProperty, value ? "PrimaryTextBrush" : "BorderStrongBrush");
            _text.SetResourceReference(TextBlock.ForegroundProperty, value ? "OnPrimaryBrush" : "TextBrush");
        }
    }

    public string? Key
    {
        get => _key;
        set
        {
            _key = value;
            bool none = string.IsNullOrEmpty(value);
            _text.Text = none ? "No key" : value!;
            _cap.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            _dashed.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            _text.SetResourceReference(TextBlock.ForegroundProperty, none ? "TextMutedBrush" : "TextBrush");
        }
    }
}
