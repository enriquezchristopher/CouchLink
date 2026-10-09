using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>
/// A small turning ring for "looking" and "connecting". It turns while visible, even when Windows
/// animations are off: it is the only sign the app is still working, so it is not decoration.
/// </summary>
internal sealed class Spinner : Grid
{
    private readonly RotateTransform _turn = new();

    public Spinner()
    {
        Width = Height = 16;
        VerticalAlignment = VerticalAlignment.Center;
        var track = new Ellipse { StrokeThickness = 2 };
        track.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        var arc = new Path
        {
            Data = Geometry.Parse("M8,1 A7,7 0 0 1 15,8"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _turn,
            Width = 16,
            Height = 16,
        };
        arc.SetResourceReference(Shape.StrokeProperty, "PrimaryTextBrush");
        Children.Add(track);
        Children.Add(arc);
        IsVisibleChanged += (_, _) => Animate(IsVisible);
    }

    internal RotateTransform Turn => _turn;

    private void Animate(bool on)
    {
        if (on)
            _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            _turn.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
