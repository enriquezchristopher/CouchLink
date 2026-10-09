using System.Windows;
using System.Windows.Controls;

namespace CouchLink.App.Ui;

public enum PillKind { Live, Reconnecting, Neutral }

/// <summary>Short status text in a pill: "● Live", "Reconnecting", "Different CouchLink version".</summary>
internal sealed class StatusPill : Border
{
    private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold };
    private PillKind _kind;

    public StatusPill()
    {
        Child = _text;
        Padding = new Thickness(9, 3, 9, 3);
        VerticalAlignment = VerticalAlignment.Center;
        Kind = PillKind.Neutral;
        // Round ends need a radius of exactly half the height: WPF shrinks a larger radius in proportion
        // to both sides, which turns a wide pill into an ellipse.
        SizeChanged += (_, e) => CornerRadius = new CornerRadius(e.NewSize.Height / 2);
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    public PillKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            (string fill, string text) = value switch
            {
                PillKind.Live => ("LivePillFillBrush", "SuccessBrush"),
                PillKind.Reconnecting => ("ReconnectingPillFillBrush", "WarningBrush"),
                _ => ("RaisedBrush", "TextMutedBrush"),
            };
            SetResourceReference(BackgroundProperty, fill);
            _text.SetResourceReference(TextBlock.ForegroundProperty, text);
        }
    }
}
