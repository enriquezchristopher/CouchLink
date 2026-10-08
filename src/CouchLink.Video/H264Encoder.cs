using CouchLink.Core.Video;
using FFmpeg.AutoGen;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace CouchLink.Video;

/// <summary>
/// H.264 through FFmpeg. Hardware encoders (AMF, NVENC) get D3D11 NV12 frames filled on the
/// GPU by <see cref="Nv12Converter"/>; x264 gets a CPU copy converted by swscale. Keyframes
/// only when forced (gop = int.MaxValue), no B-frames, one packet per frame.
/// </summary>
public sealed unsafe class H264Encoder : IFrameEncoder
{
    private readonly DesktopCapture _capture;
    private readonly int _sourceWidth, _sourceHeight;
    private readonly Nv12Converter? _converter;
    private readonly ID3D11Texture2D? _staging;
    private AVBufferRef* _hwDevice;
    private AVBufferRef* _hwFrames;
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private SwsContext* _sws;
    private long _pts;

    public H264Encoder(DesktopCapture capture, string name, VideoSize size, int frameRate, long bitRate)
    {
        _capture = capture;
        _sourceWidth = capture.Width;
        _sourceHeight = capture.Height;
        Name = name;
        IsHardware = EncoderChoice.IsHardware(name);
        Size = size;
        try
        {
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(EncoderChoice.CodecOf(name));
            if (codec == null)
                throw new FfmpegException($"FFmpeg has no {EncoderChoice.CodecOf(name)} encoder.");

            _codec = ffmpeg.avcodec_alloc_context3(codec);
            _codec->width = size.Width;
            _codec->height = size.Height;
            _codec->time_base = new AVRational { num = 1, den = frameRate };
            _codec->framerate = new AVRational { num = frameRate, den = 1 };
            _codec->bit_rate = bitRate;
            _codec->max_b_frames = 0;
            _codec->gop_size = int.MaxValue; // keyframes only when forced (0 would make x264 all-intra)
            _codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _codec->color_range = AVColorRange.AVCOL_RANGE_MPEG;

            if (IsHardware)
            {
                _codec->colorspace = AVColorSpace.AVCOL_SPC_BT709;
                _codec->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
                _codec->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
                OpenGpuFrames(size);
                _codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                _codec->hw_frames_ctx = ffmpeg.av_buffer_ref(_hwFrames);
                _converter = new Nv12Converter(capture.Device, capture.Context, capture.LastFrame, _sourceWidth, _sourceHeight, size);
            }
            else
            {
                _codec->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M; // swscale's default matrix is BT.601
                _codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
                _staging = capture.Device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)_sourceWidth,
                    Height = (uint)_sourceHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    CPUAccessFlags = CpuAccessFlags.Read,
                });
                _sws = ffmpeg.sws_getContext(_sourceWidth, _sourceHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
                    size.Width, size.Height, AVPixelFormat.AV_PIX_FMT_NV12, 2 /* SWS_BILINEAR */, null, null, null);
                if (_sws == null)
                    throw new FfmpegException("Creating the colour converter failed.");
            }

            AVDictionary* options = null;
            foreach (var (key, value) in EncoderChoice.Options(name))
                ffmpeg.av_dict_set(&options, key, value, 0);
            int result = ffmpeg.avcodec_open2(_codec, codec, &options);
            ffmpeg.av_dict_free(&options);
            FfmpegLibrary.Check(result, $"Opening {name}");
            _packet = ffmpeg.av_packet_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Name { get; }
    public bool IsHardware { get; }
    public VideoSize Size { get; }

    private void OpenGpuFrames(VideoSize size)
    {
        _hwDevice = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        var deviceContext = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)_hwDevice->data)->hwctx;
        _capture.Device.AddRef(); // FFmpeg releases it when the device context is freed
        deviceContext->device = (FFmpeg.AutoGen.ID3D11Device*)_capture.Device.NativePointer;
        FfmpegLibrary.Check(ffmpeg.av_hwdevice_ctx_init(_hwDevice), "Sharing the D3D11 device with FFmpeg");

        _hwFrames = ffmpeg.av_hwframe_ctx_alloc(_hwDevice);
        var frames = (AVHWFramesContext*)_hwFrames->data;
        frames->format = AVPixelFormat.AV_PIX_FMT_D3D11;
        frames->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
        frames->width = size.Width;
        frames->height = size.Height;
        frames->initial_pool_size = 0; // one texture per frame: AMD can't render to NV12 texture arrays
        ((AVD3D11VAFramesContext*)frames->hwctx)->BindFlags = (uint)BindFlags.RenderTarget;
        FfmpegLibrary.Check(ffmpeg.av_hwframe_ctx_init(_hwFrames), "Creating GPU frames");
    }

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        if (_capture.Width != _sourceWidth || _capture.Height != _sourceHeight)
            throw new InvalidOperationException("The screen size changed; open a new encoder.");

        AVFrame* input = ffmpeg.av_frame_alloc();
        try
        {
            if (IsHardware)
            {
                FfmpegLibrary.Check(ffmpeg.av_hwframe_get_buffer(_hwFrames, input, 0), "Getting a GPU frame");
                _converter!.Convert((nint)input->data[0], (int)(nint)input->data[1]);
            }
            else
            {
                CopyToCpu(input);
            }
            input->pts = _pts++;
            if (forceKeyframe)
                input->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
            FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(_codec, input), $"Encoding with {Name}");
        }
        finally
        {
            ffmpeg.av_frame_free(&input);
        }

        using var output = new MemoryStream();
        bool keyframe = false;
        while (true)
        {
            int result = ffmpeg.avcodec_receive_packet(_codec, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                break;
            FfmpegLibrary.Check(result, $"Reading a packet from {Name}");
            output.Write(new ReadOnlySpan<byte>(_packet->data, _packet->size));
            keyframe |= (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
            ffmpeg.av_packet_unref(_packet);
        }

        frame = output.Length == 0 ? default : new EncodedFrame(output.ToArray(), keyframe);
        return output.Length > 0;
    }

    private void CopyToCpu(AVFrame* input)
    {
        _capture.Context.CopyResource(_staging!, _capture.LastFrame);
        var map = _capture.Context.Map(_staging!, 0, MapMode.Read);
        try
        {
            input->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
            input->width = Size.Width;
            input->height = Size.Height;
            FfmpegLibrary.Check(ffmpeg.av_frame_get_buffer(input, 0), "Allocating a frame");
            byte*[] source = [(byte*)map.DataPointer];
            int[] stride = [(int)map.RowPitch];
            ffmpeg.sws_scale(_sws, source, stride, 0, _sourceHeight, input->data.ToArray(), input->linesize.ToArray());
        }
        finally
        {
            _capture.Context.Unmap(_staging!, 0);
        }
    }

    public void Dispose()
    {
        _converter?.Dispose(); // its views point into FFmpeg's textures: release them first
        _staging?.Dispose();
        if (_packet != null) { var packet = _packet; ffmpeg.av_packet_free(&packet); _packet = null; }
        if (_codec != null) { var codec = _codec; ffmpeg.avcodec_free_context(&codec); _codec = null; }
        if (_hwFrames != null) { var frames = _hwFrames; ffmpeg.av_buffer_unref(&frames); _hwFrames = null; }
        if (_hwDevice != null) { var device = _hwDevice; ffmpeg.av_buffer_unref(&device); _hwDevice = null; }
        if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
    }
}
