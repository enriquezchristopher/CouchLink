using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>A stroke icon from Theme/Icons.xaml, drawn on a 24×24 grid and scaled to Width × Height (18 by default).</summary>
internal sealed class Glyph : Viewbox
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Glyph),
        new PropertyMetadata(null, (d, e) => ((Glyph)d)._path.Data = (Geometry?)e.NewValue));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(Glyph),
        new PropertyMetadata(null, (d, e) => ((Glyph)d).UseBrush((Brush?)e.NewValue)));

    private readonly Path _path = new()
    {
        StrokeThickness = 2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public Glyph()
    {
        Width = 18;
        Height = 18;
        Stretch = Stretch.Uniform;
        Focusable = false;
        IsHitTestVisible = false;
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_path);
        Child = canvas;
        _path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private void UseBrush(Brush? brush)
    {
        if (brush is null)
            _path.SetResourceReference(Shape.StrokeProperty, "TextBrush");
        else
            _path.Stroke = brush;
    }
}
