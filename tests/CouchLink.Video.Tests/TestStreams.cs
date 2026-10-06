using CouchLink.Video;
using FFmpeg.AutoGen;

namespace CouchLink.Video.Tests;

/// <summary>Small H.264 streams made with x264 in memory, for decoder tests.</summary>
internal static unsafe class TestStreams
{
    /// <summary>One Annex B packet per frame, the first a keyframe; a moving diagonal pattern.</summary>
    public static List<byte[]> X264(int width, int height, int frames, AVColorSpace colorSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED)
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        var packets = new List<byte[]>();
        AVCodecContext* ctx = null;
        AVFrame* frame = null;
        AVPacket* packet = null;
        try
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
            ctx = ffmpeg.avcodec_alloc_context3(codec);
            ctx->width = width;
            ctx->height = height;
            ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            ctx->time_base = new AVRational { num = 1, den = 60 };
            ctx->gop_size = int.MaxValue;
            ctx->max_b_frames = 0;
            ctx->colorspace = colorSpace;
            ctx->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "preset", "ultrafast", 0);
            ffmpeg.av_dict_set(&options, "tune", "zerolatency", 0);
            int opened = ffmpeg.avcodec_open2(ctx, codec, &options);
            ffmpeg.av_dict_free(&options);
            FfmpegLibrary.Check(opened, "Opening libx264");

            frame = ffmpeg.av_frame_alloc();
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;
            FfmpegLibrary.Check(ffmpeg.av_frame_get_buffer(frame, 0), "Allocating a frame");
            packet = ffmpeg.av_packet_alloc();

            for (int i = 0; i < frames; i++)
            {
                FfmpegLibrary.Check(ffmpeg.av_frame_make_writable(frame), "Making the frame writable");
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        frame->data[0][y * frame->linesize[0] + x] = (byte)(x + y + i * 4);
                for (int y = 0; y < height / 2; y++)
                    for (int x = 0; x < width / 2; x++)
                    {
                        frame->data[1][y * frame->linesize[1] + x] = 128;
                        frame->data[2][y * frame->linesize[2] + x] = 128;
                    }
                frame->pts = i;
                FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(ctx, frame), "Encoding");
                Drain(ctx, packet, packets);
            }
            FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(ctx, null), "Flushing");
            Drain(ctx, packet, packets);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.avcodec_free_context(&ctx);
        }
        Assert.Equal(frames, packets.Count); // zerolatency: one packet per frame
        return packets;
    }

    private static void Drain(AVCodecContext* ctx, AVPacket* packet, List<byte[]> packets)
    {
        while (ffmpeg.avcodec_receive_packet(ctx, packet) == 0)
        {
            packets.Add(new ReadOnlySpan<byte>(packet->data, packet->size).ToArray());
            ffmpeg.av_packet_unref(packet);
        }
    }
}
