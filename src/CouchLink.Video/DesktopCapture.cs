using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace CouchLink.Video;

/// <summary>
/// DXGI Desktop Duplication of the host's display. Each new screen image is copied on the GPU
/// into <see cref="LastFrame"/>, so it can be re-encoded while the screen is still. When
/// duplication is lost (UAC prompt, display mode change, exclusive fullscreen) it reports
/// Lost and tries to start again on every call; a new screen size replaces LastFrame.
/// </summary>
public sealed class DesktopCapture : IScreenCapture
{
    private readonly IDXGIOutput1 _output;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D _lastFrame;

    private DesktopCapture(IDXGIAdapter1 adapter, IDXGIOutput1 output)
    {
        _output = output;
        VendorId = adapter.Description1.VendorId;
        AdapterName = adapter.Description1.Description;
        D3D11CreateDevice(adapter, DriverType.Unknown,
            DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;

        var bounds = output.Description.DesktopCoordinates;
        Width = bounds.Right - bounds.Left;
        Height = bounds.Bottom - bounds.Top;
        _lastFrame = CreateFrameTexture();
        TryStartDuplication();
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public uint VendorId { get; }
    public string AdapterName { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int RefreshRate { get; private set; } = 60;

    /// <summary>The latest screen image (BGRA). A new texture after the screen size changes.</summary>
    public ID3D11Texture2D LastFrame => _lastFrame;

    /// <summary>Opens the first display output of the first adapter that has one.</summary>
    public static DesktopCapture Open()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter)
            {
                if (adapter!.EnumOutputs(0, out IDXGIOutput? output).Success)
                {
                    using (output)
                        return new DesktopCapture(adapter, output!.QueryInterface<IDXGIOutput1>());
                }
            }
        }
        throw new InvalidOperationException("No display output was found to capture.");
    }

    public CaptureStatus TryCapture(TimeSpan timeout)
    {
        if (_duplication is null && !TryStartDuplication())
            return CaptureStatus.Lost;

        var result = _duplication!.AcquireNextFrame(
            (uint)Math.Max(0, timeout.TotalMilliseconds), out _, out IDXGIResource? resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            return CaptureStatus.NoChange;
        if (result.Failure)
        {
            StopDuplication();
            return CaptureStatus.Lost;
        }

        using (resource)
        using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
            Context.CopyResource(_lastFrame, texture);
        _duplication.ReleaseFrame();
        return CaptureStatus.NewFrame;
    }

    private bool TryStartDuplication()
    {
        try
        {
            _duplication = _output.DuplicateOutput(Device);
        }
        catch (SharpGenException)
        {
            return false; // e.g. the secure desktop (UAC) is showing; try again next call
        }

        var mode = _duplication.Description.ModeDescription;
        if (mode.RefreshRate.Denominator != 0)
            RefreshRate = (int)Math.Round((double)mode.RefreshRate.Numerator / mode.RefreshRate.Denominator);
        if ((int)mode.Width != Width || (int)mode.Height != Height)
        {
            Width = (int)mode.Width;
            Height = (int)mode.Height;
            _lastFrame.Dispose();
            _lastFrame = CreateFrameTexture();
        }
        return true;
    }

    private void StopDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;
    }

    private ID3D11Texture2D CreateFrameTexture() => Device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)Width,
        Height = (uint)Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
    });

    public void Dispose()
    {
        StopDuplication();
        _lastFrame.Dispose();
        _output.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
