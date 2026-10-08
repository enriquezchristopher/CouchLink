namespace CouchLink.Core.Video;

/// <summary>
/// How much the host spends on picture quality: a bitrate factor on top of resolution and frame
/// rate, and, at High and Max, a slower encoder setting. Balanced is today's stream and the default.
/// </summary>
public enum StreamQuality
{
    Low = -1,
    Balanced = 0,
    High = 1,
    Max = 2,
}

public static class StreamQualities
{
    public static IReadOnlyList<StreamQuality> All { get; } =
        [StreamQuality.Low, StreamQuality.Balanced, StreamQuality.High, StreamQuality.Max];

    public static string Label(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => "Low",
        StreamQuality.Balanced => "Balanced",
        StreamQuality.High => "High",
        StreamQuality.Max => "Max",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}."),
    };

    /// <summary>Multiplies the 10 Mbps at 1080p60 base bitrate.</summary>
    public static double Factor(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => 0.6,
        StreamQuality.Balanced => 1,
        StreamQuality.High => 2.5,
        StreamQuality.Max => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}."),
    };

    /// <summary>High and Max try the hardware encoder's slower, more detailed setting first.</summary>
    public static bool UsesSlowerEncoder(StreamQuality quality) =>
        quality is StreamQuality.High or StreamQuality.Max;
}
