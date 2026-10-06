using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class LetterboxTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1920, 1080)]   // same size
    [InlineData(1920, 1080, 2560, 1080, 320, 0, 1920, 1080)] // pillarbox on an ultrawide
    [InlineData(2560, 1080, 1920, 1080, 0, 135, 1920, 810)]  // letterbox an ultrawide stream
    [InlineData(1280, 720, 1920, 1080, 0, 0, 1920, 1080)]    // scale up
    [InlineData(1706, 720, 1366, 768, 0, 95, 1366, 577)]   // 576.5 rows round up
    public void Fits_the_picture_centred_keeping_its_shape(int pw, int ph, int ww, int wh, int x, int y, int w, int h)
    {
        Assert.Equal(new PixelRect(x, y, w, h), Letterbox.Fit(pw, ph, ww, wh));
    }

    [Fact]
    public void Never_returns_an_empty_rectangle()
    {
        var r = Letterbox.Fit(4000, 10, 100, 100);
        Assert.True(r.Width >= 1 && r.Height >= 1);
    }
}
