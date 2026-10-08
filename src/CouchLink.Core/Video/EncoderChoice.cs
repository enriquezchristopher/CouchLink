namespace CouchLink.Core.Video;

/// <summary>
/// Which H.264 encoder the host uses, and how. Picked by the display adapter's vendor; the
/// software encoder is always the last resort, because a hardware encoder can fail to open
/// (no such GPU, an old driver). Options are the spec's low-latency settings plus what the
/// Plan 4 spike found: AMF queues 16 frames unless async_depth=1, and both hardware encoders
/// need forced IDR for a forced I-frame to be a real keyframe. High and Max quality try a slower,
/// more detailed setting of the hardware encoder first, then fall back to the fast one.
/// </summary>
public static class EncoderChoice
{
    public const uint AmdVendorId = 0x1002;
    public const uint NvidiaVendorId = 0x10DE;

    public const string Amf = "h264_amf";
    /// <summary>AMF with usage=lowlatency, for older AMD encoders (VCE, e.g. the RX 550) that refuse ultralowlatency.</summary>
    public const string AmfLowLatency = "h264_amf (lowlatency)";
    /// <summary>AMF with quality=balanced, for High and Max stream quality.</summary>
    public const string AmfBalanced = "h264_amf (balanced)";
    public const string Nvenc = "h264_nvenc";
    /// <summary>NVENC with preset p3 instead of p1, for High and Max stream quality.</summary>
    public const string NvencP3 = "h264_nvenc (p3)";
    public const string Software = "libx264";

    public const string SoftwareWarning = "No hardware encoder - may lag with heavy games.";

    public static IReadOnlyList<string> Candidates(uint vendorId, StreamQuality quality = StreamQuality.Balanced)
    {
        bool slower = StreamQualities.UsesSlowerEncoder(quality);
        return vendorId switch
        {
            AmdVendorId => slower ? [AmfBalanced, Amf, AmfLowLatency, Software] : [Amf, AmfLowLatency, Software],
            NvidiaVendorId => slower ? [NvencP3, Nvenc, Software] : [Nvenc, Software],
            _ => [Software],
        };
    }

    public static bool IsHardware(string encoder) => CodecOf(encoder) != Software;

    /// <summary>The FFmpeg encoder a candidate opens; a candidate is an encoder plus a set of options.</summary>
    public static string CodecOf(string encoder) => encoder switch
    {
        AmfLowLatency or AmfBalanced => Amf,
        NvencP3 => Nvenc,
        _ => encoder,
    };

    public static IReadOnlyList<(string Name, string Value)> Options(string encoder) => encoder switch
    {
        Amf => AmfOptions("ultralowlatency"),
        AmfLowLatency => AmfOptions("lowlatency"),
        AmfBalanced => [.. AmfOptions("ultralowlatency"), ("quality", "balanced")],
        Nvenc => NvencOptions("p1"),
        NvencP3 => NvencOptions("p3"),
        Software => [("preset", "ultrafast"), ("tune", "zerolatency")],
        _ => throw new ArgumentException($"Unknown encoder {encoder}.", nameof(encoder)),
    };

    private static (string Name, string Value)[] AmfOptions(string usage) =>
    [
        ("usage", usage), ("rc", "vbr_latency"), ("preanalysis", "false"),
        ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1"),
    ];

    private static (string Name, string Value)[] NvencOptions(string preset) =>
        [("preset", preset), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")];
}
