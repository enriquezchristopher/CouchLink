using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// The video processor's input colour space for a stream. The host tags hardware streams BT.709 and
/// x264 streams BT.601 (SMPTE 170M); anything not tagged BT.709 is treated as BT.601, limited range
/// unless tagged full.
/// </summary>
public static class StreamColor
{
    public static ColorSpaceType For(AVColorSpace space, AVColorRange range)
    {
        bool full = range == AVColorRange.AVCOL_RANGE_JPEG;
        return space == AVColorSpace.AVCOL_SPC_BT709
            ? full ? ColorSpaceType.YcbcrFullG22LeftP709 : ColorSpaceType.YcbcrStudioG22LeftP709
            : full ? ColorSpaceType.YcbcrFullG22LeftP601 : ColorSpaceType.YcbcrStudioG22LeftP601;
    }
}
