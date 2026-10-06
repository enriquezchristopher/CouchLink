using CouchLink.Core.Video;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// Converts the BGRA screen image to NV12 and scales it, on the GPU's video processor,
/// straight into the textures of FFmpeg's frame pool (zero CPU copy). BT.709 limited range.
/// </summary>
internal sealed class Nv12Converter : IDisposable
{
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11VideoProcessorInputView _input;
    private readonly Dictionary<(nint Texture, int Slice), ID3D11VideoProcessorOutputView> _outputs = [];

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D source,
        int sourceWidth, int sourceHeight, VideoSize output)
    {
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)sourceWidth,
            InputHeight = (uint)sourceHeight,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)output.Width,
            OutputHeight = (uint)output.Height,
            Usage = VideoUsage.PlaybackNormal,
        });
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        using (var context1 = context.QueryInterface<ID3D11VideoContext1>())
        {
            context1.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
            context1.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.YcbcrStudioG22LeftP709);
        }
        _input = _videoDevice.CreateVideoProcessorInputView(source, _enumerator, new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        });
    }

    /// <summary>Writes the current source image into one NV12 texture of FFmpeg's pool.</summary>
    public void Convert(nint nv12Texture, int arraySlice)
    {
        if (!_outputs.TryGetValue((nv12Texture, arraySlice), out var view))
        {
            var texture = new ID3D11Texture2D(nv12Texture); // owned by FFmpeg's pool; never disposed here
            view = _videoDevice.CreateVideoProcessorOutputView(texture, _enumerator, new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayVideoProcessorOutputView
                {
                    MipSlice = 0, FirstArraySlice = (uint)arraySlice, ArraySize = 1,
                },
            });
            _outputs[(nv12Texture, arraySlice)] = view;
        }
        _videoContext.VideoProcessorBlt(_processor, view, 0, 1,
            [new VideoProcessorStream { Enable = true, InputSurface = _input }]).CheckError();
    }

    public void Dispose()
    {
        foreach (var view in _outputs.Values)
            view.Dispose();
        _input.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
