namespace CouchLink.Core.Video;

/// <summary>
/// Which H.264 encoder the host uses, and how. Picked by the display adapter's vendor; the
/// software encoder is always the last resort, because a hardware encoder can fail to open
/// (no such GPU, an old driver). Options are the spec's low-latency settings plus what the
/// Plan 4 spike found: AMF queues 16 frames unless async_depth=1, and both hardware encoders
/// need forced IDR for a forced I-frame to be a real keyframe.
/// </summary>
public static class EncoderChoice
{
    public const uint AmdVendorId = 0x1002;
    public const uint NvidiaVendorId = 0x10DE;

    public const string Amf = "h264_amf";
    public const string Nvenc = "h264_nvenc";
    public const string Software = "libx264";

    public const string SoftwareWarning = "No hardware encoder - may lag with heavy games.";

    public static IReadOnlyList<string> Candidates(uint vendorId) => vendorId switch
    {
        AmdVendorId => [Amf, Software],
        NvidiaVendorId => [Nvenc, Software],
        _ => [Software],
    };

    public static bool IsHardware(string encoder) => encoder != Software;

    public static IReadOnlyList<(string Name, string Value)> Options(string encoder) => encoder switch
    {
        Amf =>
        [
            ("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
            ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1"),
        ],
        Nvenc => [("preset", "p1"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
        Software => [("preset", "ultrafast"), ("tune", "zerolatency")],
        _ => throw new ArgumentException($"Unknown encoder {encoder}.", nameof(encoder)),
    };
}
