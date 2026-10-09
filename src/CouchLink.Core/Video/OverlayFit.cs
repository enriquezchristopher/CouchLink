namespace CouchLink.Core.Video;

/// <summary>
/// How much to shrink an overlay panel's text so it fits the player window: a profile with right-stick keys
/// makes the F1 panel taller than a 720p or 768p screen.
/// </summary>
public static class OverlayFit
{
    /// <summary>Room kept above and below a panel: its 24 px offset from the window edge.</summary>
    public const float Margin = 24;

    /// <summary>Below half size the text gets hard to read; past that the panel is clipped instead.</summary>
    public const float MinScale = 0.5f;

    /// <summary>1 when the text fits, else the factor that makes it fit (never below <see cref="MinScale"/>).</summary>
    public static float Scale(float textHeight, float windowHeight)
    {
        float room = windowHeight - 2 * Margin;
        if (textHeight <= 0 || room <= 0 || textHeight <= room)
            return 1f;
        return Math.Max(MinScale, room / textHeight);
    }
}
