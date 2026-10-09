using System.Windows.Media;
using CouchLink.App.Ui;

namespace CouchLink.App.Tests;

public class PlayerColorsTests
{
    [Theory]
    [InlineData(1, 0xC4, 0xB5, 0xFD)]
    [InlineData(2, 0xFD, 0xA4, 0xAF)]
    [InlineData(3, 0xFC, 0xD3, 0x4D)]
    [InlineData(10, 0xA5, 0xB4, 0xFC)]
    public void Slots_have_the_spec_colors(byte slot, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), PlayerColors.ColorFor(slot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slots_outside_1_to_10_have_no_color(byte slot)
    {
        Assert.Null(PlayerColors.ColorFor(slot));
        Assert.Equal(Color.FromRgb(0x27, 0x27, 0x3B), ((SolidColorBrush)PlayerColors.BrushFor(slot)).Color);
    }

    [Fact]
    public void All_ten_colors_differ()
    {
        var colors = Enumerable.Range(1, 10).Select(s => PlayerColors.ColorFor((byte)s)).ToList();
        Assert.Equal(10, colors.Distinct().Count());
    }

    [Fact]
    public void Dark_text_on_every_player_color_is_at_least_9_5_to_1()
    {
        var text = ((SolidColorBrush)PlayerColors.TextOnPlayer).Color;
        for (byte slot = 1; slot <= 10; slot++)
            Assert.True(Contrast(PlayerColors.ColorFor(slot)!.Value, text) >= 9.5, $"P{slot}");
    }

    [Fact]
    public void Brushes_are_frozen_so_any_thread_can_use_them()
    {
        Assert.True(PlayerColors.BrushFor(4).IsFrozen);
        Assert.True(PlayerColors.TextOnPlayer.IsFrozen);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte v)
    {
        double s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
