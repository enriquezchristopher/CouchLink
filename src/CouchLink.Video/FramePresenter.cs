using System.Numerics;
using CouchLink.Core.Video;
using AVFrame = FFmpeg.AutoGen.AVFrame;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;
using D3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace CouchLink.Video;

/// <summary>
/// Shows decoded pictures in a window: the D3D11 video processor converts NV12 to BGRA, scales and
/// letterboxes straight into a flip-model swap chain's back buffer, Direct2D draws any text on top,
/// and Present(0, AllowTearing) shows it without waiting for vsync. Software pictures are uploaded
/// through a staging texture first. Used from the player thread.
/// </summary>
public sealed unsafe class FramePresenter : IFramePresenter
{
    private const int MaxCachedInputs = 32;

    private readonly D3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly bool _tearing;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext1 _videoContext;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _d2d;
    private readonly IDWriteFactory _dwrite;
    private readonly IDWriteTextFormat _statsFont, _statusFont;
    private readonly ID2D1SolidColorBrush _text, _panel;
    private readonly Dictionary<(nint Texture, uint Slice), ID3D11VideoProcessorInputView> _inputs = [];
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private D3D11Texture2D? _staging, _upload;
    private ID3D11VideoProcessorInputView? _uploadView;
    private (int Width, int Height) _picture, _window;

    public FramePresenter(D3D11Device device, ID3D11DeviceContext context, nint window, int width, int height)
    {
        _device = device;
        _context = context;
        _window = (width, height);
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory5>(false))
        {
            _tearing = factory.PresentAllowTearing;
            _swapChain = factory.CreateSwapChainForHwnd(device, window, new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                Flags = SwapFlags,
            });
            factory.MakeWindowAssociation(window, WindowAssociationFlags.IgnoreAltEnter);
        }
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext1>();

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _statsFont = _dwrite.CreateTextFormat("Consolas", FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, 18);
        _statusFont = _dwrite.CreateTextFormat("Segoe UI", FontWeight.SemiBold, FontStyle.Normal, FontStretch.Normal, 32);
        _text = _d2d.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _panel = _d2d.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0.65f));
    }

    private SwapChainFlags SwapFlags => _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;

    /// <summary>The window's client area changed size.</summary>
    public void Resize(int width, int height)
    {
        if ((width, height) == _window || width <= 0 || height <= 0)
            return;
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.Unknown, SwapFlags).CheckError();
        _window = (width, height); // only once the buffers really are that size
        ResetProcessor(); // output size changed
    }

    public void Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint)
    {
        using (var back = _swapChain.GetBuffer<D3D11Texture2D>(0))
        {
            if (picture is { } p)
                Blt(p, back);
            if (picture is null || status is not null || stats is not null || controls is not null || hint is not null)
                DrawText(back, clear: picture is null, status, stats, controls, hint);
        }
        _swapChain.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();
    }

    private void Blt(DecodedPicture p, D3D11Texture2D back)
    {
        EnsureProcessor(p.Width, p.Height);
        var input = p.OnGpu ? GpuInput((AVFrame*)p.Frame) : Upload((AVFrame*)p.Frame);
        _videoContext.VideoProcessorSetStreamColorSpace1(_processor!, 0, p.Color);
        using var output = _videoDevice.CreateVideoProcessorOutputView(back, _enumerator!, new VideoProcessorOutputViewDescription
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
        });
        _videoContext.VideoProcessorBlt(_processor!, output, 0, 1,
            [new VideoProcessorStream { Enable = true, InputSurface = input }]).CheckError();
    }

    private void EnsureProcessor(int width, int height)
    {
        if (_processor is not null && _picture == (width, height))
            return;
        ResetProcessor();
        _picture = (width, height);
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)_window.Width,
            OutputHeight = (uint)_window.Height,
            Usage = VideoUsage.PlaybackNormal,
        });
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        var fit = Letterbox.Fit(width, height, _window.Width, _window.Height);
        _videoContext.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.RgbFullG22NoneP709);
        _videoContext.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true, new Vortice.RawRect(0, 0, width, height));
        _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, true,
            new Vortice.RawRect(fit.X, fit.Y, fit.X + fit.Width, fit.Y + fit.Height));
        _videoContext.VideoProcessorSetOutputTargetRect(_processor, true, new Vortice.RawRect(0, 0, _window.Width, _window.Height));
        _videoContext.VideoProcessorSetOutputBackgroundColor(_processor, false,
            new Vortice.Direct3D11.VideoColor { Rgba = new VideoColorRgba { R = 0, G = 0, B = 0, A = 1 } });
    }

    private ID3D11VideoProcessorInputView GpuInput(AVFrame* frame)
    {
        var key = ((nint)frame->data[0], (uint)(nint)frame->data[1]);
        if (_inputs.TryGetValue(key, out var view))
            return view;
        if (_inputs.Count >= MaxCachedInputs)
            ClearInputs(); // a new decoder's textures; the old ones can go
        var texture = new D3D11Texture2D(key.Item1); // owned by FFmpeg's pool; never disposed here
        view = _videoDevice.CreateVideoProcessorInputView(texture, _enumerator!, new VideoProcessorInputViewDescription
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = key.Item2 },
        });
        _inputs[key] = view;
        return view;
    }

    /// <summary>Copies a software picture's planes into an NV12 texture the video processor can read.</summary>
    private ID3D11VideoProcessorInputView Upload(AVFrame* frame)
    {
        int w = frame->width, h = frame->height;
        if (_upload is null)
        {
            var description = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Write,
            };
            _staging = _device.CreateTexture2D(description);
            description.Usage = ResourceUsage.Default;
            description.CPUAccessFlags = CpuAccessFlags.None;
            description.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget; // AMD rejects ShaderResource alone
            _upload = _device.CreateTexture2D(description);
            _uploadView = _videoDevice.CreateVideoProcessorInputView(_upload, _enumerator!, new VideoProcessorInputViewDescription
            {
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            });
        }

        var map = _context.Map(_staging!, 0, MapMode.Write);
        try
        {
            byte* dst = (byte*)map.DataPointer;
            int pitch = (int)map.RowPitch;
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(frame->data[0] + y * frame->linesize[0], dst + y * pitch, w, w);
            byte* uv = dst + pitch * h; // NV12: interleaved UV rows follow the luma rows
            for (int y = 0; y < h / 2; y++)
            {
                byte* u = frame->data[1] + y * frame->linesize[1];
                byte* v = frame->data[2] + y * frame->linesize[2];
                byte* row = uv + y * pitch;
                for (int x = 0; x < w / 2; x++)
                {
                    row[2 * x] = u[x];
                    row[2 * x + 1] = v[x];
                }
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
        _context.CopyResource(_upload!, _staging!);
        return _uploadView!;
    }

    private void DrawText(D3D11Texture2D back, bool clear, string? status, string? stats, string? controls, string? hint)
    {
        using var surface = back.QueryInterface<IDXGISurface>();
        using var target = _d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));
        _d2d.Target = target;
        _d2d.BeginDraw();
        if (clear)
            _d2d.Clear(new Color4(0f, 0f, 0f, 1f));
        if (controls is not null)
            Panel(controls, _statsFont, 24, 24, alignX: 0, alignY: 0);
        if (stats is not null)
            Panel(stats, _statsFont, _window.Width - 24, 24, alignX: 1, alignY: 0);
        if (hint is not null)
            Panel(hint, _statsFont, 24, _window.Height - 24, alignX: 0, alignY: 1);
        if (status is not null)
            Panel(status, _statusFont, _window.Width / 2f, _window.Height / 2f, alignX: 0.5f, alignY: 0.5f);
        _d2d.EndDraw();
        _d2d.Target = null;
    }

    /// <summary>Text on a dark panel; (x, y) is where <paramref name="alignX"/>/<paramref name="alignY"/> of the text sits (0 = left/top, 1 = right/bottom).</summary>
    private void Panel(string text, IDWriteTextFormat font, float x, float y, float alignX, float alignY)
    {
        using var layout = _dwrite.CreateTextLayout(text, font, _window.Width, _window.Height);
        var size = layout.Metrics;
        x -= size.Width * alignX;
        y -= size.Height * alignY;
        _d2d.FillRectangle(new Rect(x - 12, y - 8, size.Width + 24, size.Height + 16), _panel);
        _d2d.DrawTextLayout(new Vector2(x, y), layout, _text);
    }

    private void ClearInputs()
    {
        foreach (var view in _inputs.Values)
            view.Dispose();
        _inputs.Clear();
    }

    private void ResetProcessor()
    {
        ClearInputs();
        _uploadView?.Dispose(); _uploadView = null;
        _upload?.Dispose(); _upload = null;
        _staging?.Dispose(); _staging = null;
        _processor?.Dispose(); _processor = null;
        _enumerator?.Dispose(); _enumerator = null;
    }

    public void Dispose()
    {
        ResetProcessor();
        _text.Dispose();
        _panel.Dispose();
        _statsFont.Dispose();
        _statusFont.Dispose();
        _dwrite.Dispose();
        _d2d.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        _swapChain.Dispose();
    }
}
