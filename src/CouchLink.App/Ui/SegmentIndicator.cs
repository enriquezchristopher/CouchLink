using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CouchLink.App.Theme;

namespace CouchLink.App.Ui;

/// <summary>
/// The purple fill behind a segmented control's checked item. It sits under the items' panel in the same
/// cell, as wide as one item, and glides to the checked item with a TranslateTransform instead of jumping.
/// The items stay plain RadioButtons for the keyboard, screen readers and tests.
/// </summary>
internal sealed class SegmentIndicator : Border
{
    private Panel? _items;

    public SegmentIndicator()
    {
        CornerRadius = new CornerRadius(6);
        HorizontalAlignment = HorizontalAlignment.Left;
        IsHitTestVisible = false;
        Visibility = Visibility.Hidden; // until the items are laid out
        RenderTransform = Slide;
        SetResourceReference(BackgroundProperty, "PrimaryBrush");
    }

    internal TranslateTransform Slide { get; } = new();

    /// <summary>Where it is going: the checked item's left edge in the panel.</summary>
    internal double TargetX { get; private set; }

    /// <summary>Follows the checked toggle in <paramref name="items"/>: glides when the choice changes, jumps when the panel resizes.</summary>
    public void Track(Panel items)
    {
        _items = items;
        items.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Follow(glide: true)));
        items.SizeChanged += (_, _) => Follow(glide: false);
    }

    private void Follow(bool glide)
    {
        if (_items?.Children.OfType<ToggleButton>().FirstOrDefault(b => b.IsChecked == true) is not { ActualWidth: > 0 } item)
        {
            Visibility = Visibility.Hidden;
            return;
        }
        Visibility = Visibility.Visible;
        if (Width != item.ActualWidth)
            Width = item.ActualWidth; // only changes when the panel resizes; never animated
        TargetX = item.TranslatePoint(new Point(), _items).X;
        if (glide)
        {
            Motion.SlideTo(Slide, TargetX);
        }
        else
        {
            Slide.BeginAnimation(TranslateTransform.XProperty, null);
            Slide.X = TargetX;
        }
    }
}
