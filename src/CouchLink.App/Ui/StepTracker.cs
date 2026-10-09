using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CouchLink.App.Ui;

/// <summary>Connect → Host lets you in → Play, on the session screen. Steps before Current are done.</summary>
internal sealed class StepTracker : Grid
{
    private static readonly string[] Names = ["Connect", "Host lets you in", "Play"];
    private readonly Ellipse[] _dots = new Ellipse[3];
    private readonly TextBlock[] _labels = new TextBlock[3];
    private readonly Border[] _lines = new Border[2];
    private int _current;

    public StepTracker()
    {
        for (int i = 0; i < 5; i++)
            ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            _dots[i] = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
            _labels[i] = new TextBlock { Text = Names[i], FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var step = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dots[i], _labels[i] } };
            SetColumn(step, i * 2);
            Children.Add(step);
        }
        for (int i = 0; i < 2; i++)
        {
            _lines[i] = new Border { Height = 2, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 12 };
            SetColumn(_lines[i], i * 2 + 1);
            Children.Add(_lines[i]);
        }
        Current = 0;
    }

    internal IReadOnlyList<Ellipse> Dots => _dots;

    public int Current
    {
        get => _current;
        set
        {
            _current = Math.Clamp(value, 0, 2);
            for (int i = 0; i < 3; i++)
            {
                _dots[i].SetResourceReference(Shape.FillProperty, i < _current ? "PrimaryBrush" : i == _current ? "PrimaryTextBrush" : "BorderBrush");
                _labels[i].SetResourceReference(TextBlock.ForegroundProperty, i == _current ? "TextBrush" : "TextMutedBrush");
                _labels[i].FontWeight = i == _current ? FontWeights.SemiBold : FontWeights.Normal;
            }
            for (int i = 0; i < 2; i++)
                _lines[i].SetResourceReference(Border.BackgroundProperty, i < _current ? "PrimaryBrush" : "BorderBrush");
        }
    }
}
