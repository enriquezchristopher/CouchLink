using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CouchLink.App.Ui;

/// <summary>"P3" on the player's color: small in lists, large on the session screen.</summary>
internal sealed class PlayerChip : Border
{
    private readonly TextBlock _text = new()
    {
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = PlayerColors.TextOnPlayer,
    };
    private byte _slot;
    private bool _large;

    public PlayerChip()
    {
        Child = _text;
        Resize();
        Slot = 1;
    }

    public string Text => _text.Text;

    public byte Slot
    {
        get => _slot;
        set
        {
            _slot = value;
            Background = PlayerColors.BrushFor(value);
            _text.Text = $"P{value}";
            AutomationProperties.SetName(this, $"Player {value}");
        }
    }

    public bool Large
    {
        get => _large;
        set
        {
            _large = value;
            Resize();
        }
    }

    private void Resize()
    {
        Width = Height = _large ? 52 : 28;
        CornerRadius = new CornerRadius(_large ? 14 : 8);
        _text.FontSize = _large ? 18 : 11;
    }
}
