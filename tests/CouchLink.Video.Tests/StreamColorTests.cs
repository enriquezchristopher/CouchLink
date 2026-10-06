using CouchLink.Video;
using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video.Tests;

public class StreamColorTests
{
    [Theory]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG, ColorSpaceType.YcbcrStudioG22LeftP709)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_JPEG, ColorSpaceType.YcbcrFullG22LeftP709)]
    [InlineData(AVColorSpace.AVCOL_SPC_SMPTE170M, AVColorRange.AVCOL_RANGE_MPEG, ColorSpaceType.YcbcrStudioG22LeftP601)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT470BG, AVColorRange.AVCOL_RANGE_JPEG, ColorSpaceType.YcbcrFullG22LeftP601)]
    [InlineData(AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange.AVCOL_RANGE_UNSPECIFIED, ColorSpaceType.YcbcrStudioG22LeftP601)]
    public void Stream_colour_tags_pick_the_matching_colour_space(AVColorSpace space, AVColorRange range, ColorSpaceType expected)
    {
        Assert.Equal(expected, StreamColor.For(space, range));
    }
}
