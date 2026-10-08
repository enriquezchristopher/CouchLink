using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class EncoderChoiceTests
{
    [Fact]
    public void Amd_tries_AMF_ultra_low_latency_then_AMF_low_latency_then_x264()
    {
        // Older AMD encoders (VCE, e.g. the RX 550) refuse usage=ultralowlatency; lowlatency still runs on the GPU.
        Assert.Equal(["h264_amf", "h264_amf (lowlatency)", "libx264"], EncoderChoice.Candidates(0x1002));
    }

    [Fact]
    public void AMF_low_latency_is_AMF_with_usage_lowlatency_and_the_same_other_options()
    {
        Assert.Equal(
            [("usage", "lowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
             ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1")],
            EncoderChoice.Options("h264_amf (lowlatency)"));
    }

    [Theory]
    [InlineData("h264_amf", "h264_amf")]
    [InlineData("h264_amf (lowlatency)", "h264_amf")]
    [InlineData("h264_nvenc", "h264_nvenc")]
    [InlineData("libx264", "libx264")]
    [InlineData("h264_amf (balanced)", "h264_amf")]
    [InlineData("h264_nvenc (p3)", "h264_nvenc")]
    public void Each_candidate_names_the_FFmpeg_encoder_it_opens(string candidate, string codec)
    {
        Assert.Equal(codec, EncoderChoice.CodecOf(candidate));
    }

    [Fact]
    public void Nvidia_tries_NVENC_then_x264()
    {
        Assert.Equal(["h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE));
    }

    [Theory]
    [InlineData(0x8086u)] // Intel: out of scope (spec section 1)
    [InlineData(0x1414u)] // Microsoft Basic Display Adapter
    public void Other_GPUs_use_x264(uint vendor)
    {
        Assert.Equal(["libx264"], EncoderChoice.Candidates(vendor));
    }

    [Fact]
    public void Only_x264_is_software()
    {
        Assert.True(EncoderChoice.IsHardware("h264_amf"));
        Assert.True(EncoderChoice.IsHardware("h264_amf (lowlatency)"));
        Assert.True(EncoderChoice.IsHardware("h264_nvenc"));
        Assert.False(EncoderChoice.IsHardware("libx264"));
        Assert.True(EncoderChoice.IsHardware("h264_amf (balanced)"));
        Assert.True(EncoderChoice.IsHardware("h264_nvenc (p3)"));
    }

    [Fact]
    public void AMF_uses_ultra_low_latency_with_one_frame_in_flight_and_real_IDR_keyframes()
    {
        Assert.Equal(
            [("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
             ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1")],
            EncoderChoice.Options("h264_amf"));
    }

    [Fact]
    public void NVENC_uses_p1_ull_CBR_and_real_IDR_keyframes()
    {
        Assert.Equal(
            [("preset", "p1"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
            EncoderChoice.Options("h264_nvenc"));
    }

    [Fact]
    public void X264_uses_ultrafast_zerolatency()
    {
        Assert.Equal([("preset", "ultrafast"), ("tune", "zerolatency")], EncoderChoice.Options("libx264"));
    }

    [Theory]
    [InlineData(StreamQuality.High)]
    [InlineData(StreamQuality.Max)]
    public void Amd_at_high_quality_tries_AMF_balanced_first_then_the_usual_order(StreamQuality quality)
    {
        // An AMF that refuses quality=balanced still lands on the GPU, not on x264.
        Assert.Equal(["h264_amf (balanced)", "h264_amf", "h264_amf (lowlatency)", "libx264"],
            EncoderChoice.Candidates(0x1002, quality));
    }

    [Theory]
    [InlineData(StreamQuality.High)]
    [InlineData(StreamQuality.Max)]
    public void Nvidia_at_high_quality_tries_NVENC_p3_first(StreamQuality quality)
    {
        Assert.Equal(["h264_nvenc (p3)", "h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE, quality));
    }

    [Theory]
    [InlineData(StreamQuality.Low)]
    [InlineData(StreamQuality.Balanced)]
    public void Low_and_balanced_keep_todays_candidates(StreamQuality quality)
    {
        Assert.Equal(["h264_amf", "h264_amf (lowlatency)", "libx264"], EncoderChoice.Candidates(0x1002, quality));
        Assert.Equal(["h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE, quality));
    }

    [Fact]
    public void Other_GPUs_use_x264_at_every_quality()
    {
        Assert.Equal(["libx264"], EncoderChoice.Candidates(0x8086, StreamQuality.Max));
    }

    [Fact]
    public void AMF_balanced_is_AMF_ultra_low_latency_plus_quality_balanced()
    {
        Assert.Equal(
            [("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
             ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1"), ("quality", "balanced")],
            EncoderChoice.Options("h264_amf (balanced)"));
    }

    [Fact]
    public void NVENC_p3_is_NVENC_with_preset_p3()
    {
        Assert.Equal(
            [("preset", "p3"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
            EncoderChoice.Options("h264_nvenc (p3)"));
    }

    [Fact]
    public void Unknown_encoders_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => EncoderChoice.Options("h264_qsv"));
    }

    [Fact]
    public void Warning_text_matches_the_spec()
    {
        Assert.Equal("No hardware encoder - may lag with heavy games.", EncoderChoice.SoftwareWarning);
    }
}
