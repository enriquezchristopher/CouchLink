using FFmpeg.AutoGen;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;

namespace CouchLink.Video;

/// <summary>
/// FFmpeg's H.264 decoder. With a D3D11 device it decodes on the GPU (D3D11VA) into textures of that
/// device, which the presenter reads directly; without one, or if that fails, it decodes in software.
/// Low-delay: one picture out per frame in. Used from one thread.
/// </summary>
public sealed unsafe class H264Decoder : IFrameDecoder
{
    private AVCodecContext* _ctx;
    private AVBufferRef* _hwDevice;
    private AVFrame* _frame;
    private AVPacket* _packet;

    private H264Decoder(D3D11Device? device)
    {
        try
        {
            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
                throw new FfmpegException("FFmpeg has no H.264 decoder.");
            _ctx = ffmpeg.avcodec_alloc_context3(codec);
            _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            if (device is not null)
            {
                _hwDevice = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
                var deviceContext = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)_hwDevice->data)->hwctx;
                device.AddRef(); // FFmpeg releases it when the device context is freed
                deviceContext->device = (FFmpeg.AutoGen.ID3D11Device*)device.NativePointer;
                FfmpegLibrary.Check(ffmpeg.av_hwdevice_ctx_init(_hwDevice), "Sharing the D3D11 device with FFmpeg");
                _ctx->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDevice);
                _ctx->extra_hw_frames = 4; // the presenter keeps the last picture for redraws
                _ctx->thread_count = 1;
                IsHardware = true;
            }
            else
            {
                _ctx->thread_type = ffmpeg.FF_THREAD_SLICE; // frame threads would add a frame of delay each
                _ctx->thread_count = 0;
            }
            FfmpegLibrary.Check(ffmpeg.avcodec_open2(_ctx, codec, null), "Opening the H.264 decoder");
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsHardware { get; private set; }
    public string Name => IsHardware ? "D3D11VA" : "software";

    /// <summary>D3D11VA on <paramref name="device"/>, or software with the reason in <paramref name="hardwareError"/>.</summary>
    public static H264Decoder Open(D3D11Device? device, out string? hardwareError)
    {
        hardwareError = null;
        if (device is null)
        {
            hardwareError = "no D3D11 device";
            return OpenSoftware();
        }
        try
        {
            return new H264Decoder(device);
        }
        catch (Exception e) when (e is FfmpegException or SharpGen.Runtime.SharpGenException)
        {
            hardwareError = e.Message;
            return OpenSoftware();
        }
    }

    public static H264Decoder OpenSoftware() => new(null);

    public bool Decode(byte[] data, out DecodedPicture picture)
    {
        picture = default;
        ffmpeg.av_frame_unref(_frame); // the previous picture is no longer needed
        fixed (byte* bytes = data)
        {
            _packet->data = bytes;
            _packet->size = data.Length;
            int sent = ffmpeg.avcodec_send_packet(_ctx, _packet); // copies the data: no refcounted buffer
            _packet->data = null;
            _packet->size = 0;
            FfmpegLibrary.Check(sent, "Decoding a frame");
        }
        int result = ffmpeg.avcodec_receive_frame(_ctx, _frame);
        if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            return false;
        FfmpegLibrary.Check(result, "Decoding a frame");

        bool onGpu = _frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11;
        if (IsHardware && !onGpu)
            IsHardware = false; // FFmpeg fell back to software by itself (e.g. an unsupported profile)
        picture = new DecodedPicture(_frame->width, _frame->height, (nint)_frame, onGpu,
            StreamColor.For(_frame->colorspace, _frame->color_range));
        return true;
    }

    public void Dispose()
    {
        if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
        if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
        if (_ctx != null) { var c = _ctx; ffmpeg.avcodec_free_context(&c); _ctx = null; }
        if (_hwDevice != null) { var d = _hwDevice; ffmpeg.av_buffer_unref(&d); _hwDevice = null; }
    }
}
