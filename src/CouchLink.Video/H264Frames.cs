using FFmpeg.AutoGen;

namespace CouchLink.Video;

/// <summary>Splits an Annex B H.264 file (as saved by VideoTest encode or --save-video) into frames.</summary>
public static unsafe class H264Frames
{
    public static List<byte[]> Split(byte[] annexB)
    {
        var frames = new List<byte[]>();
        var parser = ffmpeg.av_parser_init(AVCodecID.AV_CODEC_ID_H264);
        var ctx = ffmpeg.avcodec_alloc_context3(ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264));
        try
        {
            fixed (byte* start = annexB)
            {
                int offset = 0;
                while (true)
                {
                    bool flushing = offset >= annexB.Length;
                    byte* output;
                    int size;
                    int used = ffmpeg.av_parser_parse2(parser, ctx, &output, &size,
                        flushing ? null : start + offset, flushing ? 0 : annexB.Length - offset,
                        ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
                    offset += used;
                    if (size > 0)
                        frames.Add(new ReadOnlySpan<byte>(output, size).ToArray());
                    else if (flushing)
                        break;
                }
            }
        }
        finally
        {
            ffmpeg.av_parser_close(parser);
            ffmpeg.avcodec_free_context(&ctx);
        }
        return frames;
    }
}
