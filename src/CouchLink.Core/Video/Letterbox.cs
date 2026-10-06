namespace CouchLink.Core.Video;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Where a picture goes in a window: as large as fits, same shape, centred, black around it.</summary>
public static class Letterbox
{
    public static PixelRect Fit(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        double scale = Math.Min((double)windowWidth / pictureWidth, (double)windowHeight / pictureHeight);
        int w = Math.Max(1, (int)Math.Round(pictureWidth * scale));
        int h = Math.Max(1, (int)Math.Round(pictureHeight * scale));
        return new PixelRect((windowWidth - w) / 2, (windowHeight - h) / 2, w, h);
    }
}
