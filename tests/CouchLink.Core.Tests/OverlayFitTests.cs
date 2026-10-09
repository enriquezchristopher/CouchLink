using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class OverlayFitTests
{
    [Theory]
    [InlineData(650, 1080)] // the F1 panel on a 1080p screen
    [InlineData(650, 720)]  // the built-in layout's F1 panel fits a 720p window
    [InlineData(672, 720)]  // exactly fills the room: 720 - 48
    public void A_panel_that_fits_keeps_its_size(float textHeight, float windowHeight)
    {
        Assert.Equal(1f, OverlayFit.Scale(textHeight, windowHeight));
    }

    [Fact]
    public void A_panel_taller_than_the_window_shrinks_to_fit()
    {
        // NBA 2K22's F1 panel (35 lines) in the 1280x720 test window
        float scale = OverlayFit.Scale(760, 720);
        Assert.Equal(672f / 760f, scale, 4);
        Assert.True(760 * scale + 2 * OverlayFit.Margin <= 720);
    }

    [Fact]
    public void Text_never_shrinks_below_half_size()
    {
        Assert.Equal(OverlayFit.MinScale, OverlayFit.Scale(760, 200));
    }

    [Fact]
    public void An_empty_or_unsized_panel_keeps_its_size()
    {
        Assert.Equal(1f, OverlayFit.Scale(0, 720));
        Assert.Equal(1f, OverlayFit.Scale(760, 0)); // window not sized yet
    }
}
