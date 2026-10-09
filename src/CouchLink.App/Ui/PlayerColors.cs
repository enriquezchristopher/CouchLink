using System.Windows.Media;

namespace CouchLink.App.Ui;

/// <summary>
/// Each player's color, P1 (the host) to P10: the lobby rows, the join list and the session screen use
/// the same one, so a player can find themselves. Fixed identity colors, not theme tokens.
/// </summary>
internal static class PlayerColors
{
    private static readonly Color[] Slots =
    [
        Rgb(0xC4B5FD), Rgb(0xFDA4AF), Rgb(0xFCD34D), Rgb(0x6EE7B7), Rgb(0x7DD3FC),
        Rgb(0xF9A8D4), Rgb(0xBEF264), Rgb(0xFDBA74), Rgb(0x5EEAD4), Rgb(0xA5B4FC),
    ];

    private static readonly Brush[] Brushes = Slots.Select(c => (Brush)Frozen(c)).ToArray();
    private static readonly Brush Unknown = Frozen(Rgb(0x27273B));

    /// <summary>Text on a player color is Background (#0F0F23). Worst case is P10 at 9.47:1.</summary>
    public static Brush TextOnPlayer { get; } = Frozen(Rgb(0x0F0F23));

    public static Color? ColorFor(byte slot) => slot is >= 1 and <= 10 ? Slots[slot - 1] : null;

    public static Brush BrushFor(byte slot) => slot is >= 1 and <= 10 ? Brushes[slot - 1] : Unknown;

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
