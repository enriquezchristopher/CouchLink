using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CouchLink.App.Theme;

namespace CouchLink.App.Ui;

public enum BannerKind { Info, Warning, Error }

/// <summary>A message across the screen: info, warning (link too slow, key moved) or error (why the last session ended).</summary>
internal sealed class Banner : Border
{
    private readonly Glyph _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private BannerKind _kind;

    public Banner()
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 8, 8, 8);
        CloseButton = new Button
        {
            Padding = new Thickness(4, 0, 4, 0),
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Content = new Glyph { Width = 14, Height = 14 },
        };
        CloseButton.SetResourceReference(StyleProperty, "GhostButton");
        AutomationProperties.SetName(CloseButton, "Dismiss");
        AutomationProperties.SetAutomationId(CloseButton, "BannerClose");
        CloseButton.Click += (_, _) => CloseClicked?.Invoke();
        ((Glyph)CloseButton.Content).SetResourceReference(Glyph.DataProperty, "IconClose");

        var row = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(CloseButton, Dock.Right);
        row.Children.Add(_icon);
        row.Children.Add(CloseButton);
        row.Children.Add(_text);
        Child = row;
        Kind = BannerKind.Info;
    }

    public event Action? CloseClicked;

    /// <summary>Shown (Visibility → Visible): it slides down into place and fades in.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == VisibilityProperty && Visibility == Visibility.Visible)
            Motion.DropIn(this);
    }

    internal Button CloseButton { get; }

    public string Text
    {
        get => _text.Text;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public bool CanClose
    {
        get => CloseButton.Visibility == Visibility.Visible;
        set => CloseButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public BannerKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            SetResourceReference(BackgroundProperty, $"{value}BannerFillBrush");
            SetResourceReference(BorderBrushProperty, $"{value}BannerBorderBrush");
            _text.SetResourceReference(TextBlock.ForegroundProperty, $"{value}BannerTextBrush");
            _icon.SetResourceReference(Glyph.BrushProperty, $"{value}BannerTextBrush");
            _icon.SetResourceReference(Glyph.DataProperty, value == BannerKind.Info ? "IconInfo" : "IconAlert");
            ((Glyph)CloseButton.Content).SetResourceReference(Glyph.BrushProperty, $"{value}BannerTextBrush");
        }
    }
}
