namespace CouchLink.Core.Video;

/// <summary>A video frame size in pixels.</summary>
public readonly record struct VideoSize(int Width, int Height)
{
    /// <summary>
    /// Scales a screen size down to at most <paramref name="maxHeight"/> lines, keeping the
    /// aspect ratio, never scaling up. Both sides are even, as H.264 4:2:0 needs.
    /// </summary>
    public static VideoSize ForStream(int screenWidth, int screenHeight, int maxHeight)
    {
        if (screenWidth < 2 || screenHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(screenWidth), "Screen must be at least 2x2.");
        if (maxHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(maxHeight), "Height limit must be at least 2.");

        int height = Math.Min(screenHeight, maxHeight);
        int width = (int)Math.Round(screenWidth * (double)height / screenHeight);
        return new VideoSize(width & ~1, height & ~1);
    }
}
