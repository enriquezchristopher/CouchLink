using CouchLink.Video;
using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video.Tests;

public class H264DecoderTests
{
    [Fact]
    public void Falls_back_to_software_without_a_device()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        using var decoder = H264Decoder.Open(device: null, out var hardwareError);

        Assert.False(decoder.IsHardware);
        Assert.Equal("software", decoder.Name);
        Assert.Contains("no D3D11 device", hardwareError);
    }

    [Fact]
    public void Decodes_every_frame_at_its_size_with_no_delay()
    {
        var packets = TestStreams.X264(320, 240, frames: 10);
        using var decoder = H264Decoder.OpenSoftware();

        foreach (var packet in packets)
        {
            Assert.True(decoder.Decode(packet, out var picture)); // one frame out per packet in
            Assert.Equal((320, 240), (picture.Width, picture.Height));
            Assert.False(picture.OnGpu);
            Assert.NotEqual(0, picture.Frame);
        }
    }

    [Fact]
    public void Pictures_carry_the_streams_colour_space()
    {
        var bt709 = TestStreams.X264(64, 64, 1, AVColorSpace.AVCOL_SPC_BT709);
        var untagged = TestStreams.X264(64, 64, 1);
        using var a = H264Decoder.OpenSoftware();
        using var b = H264Decoder.OpenSoftware();

        Assert.True(a.Decode(bt709[0], out var p709));
        Assert.True(b.Decode(untagged[0], out var p601));

        Assert.Equal(ColorSpaceType.YcbcrStudioG22LeftP709, p709.Color);
        Assert.Equal(ColorSpaceType.YcbcrStudioG22LeftP601, p601.Color);
    }

    [Fact]
    public void Garbage_is_an_FfmpegException_or_nothing_never_a_crash()
    {
        using var decoder = H264Decoder.OpenSoftware();
        var garbage = new byte[2000];
        new Random(1).NextBytes(garbage);
        garbage[0] = 0; garbage[1] = 0; garbage[2] = 1; garbage[3] = 0x65; // looks like a slice

        var e = Record.Exception(() => decoder.Decode(garbage, out _));

        Assert.True(e is null or FfmpegException, e?.ToString());
    }

    [Fact]
    public void Decoding_recovers_at_the_next_keyframe_after_garbage()
    {
        var stream = TestStreams.X264(160, 120, frames: 3);
        using var decoder = H264Decoder.OpenSoftware();
        var garbage = new byte[500];
        new Random(2).NextBytes(garbage);
        try { decoder.Decode(garbage, out _); } catch (FfmpegException) { }

        Assert.True(decoder.Decode(stream[0], out var picture)); // the keyframe
        Assert.Equal(160, picture.Width);
    }

    [Fact]
    public unsafe void A_decode_without_a_new_picture_keeps_the_last_one()
    {
        var stream = TestStreams.X264(160, 120, frames: 1);
        using var decoder = H264Decoder.OpenSoftware();
        Assert.True(decoder.Decode(stream[0], out var picture));
        var garbage = new byte[500];
        new Random(3).NextBytes(garbage);

        try { decoder.Decode(garbage, out _); } catch (FfmpegException) { }

        // The player redraws the last picture until a new one comes; it must still be there.
        Assert.Equal(160, ((FFmpeg.AutoGen.AVFrame*)picture.Frame)->width);
    }
}
