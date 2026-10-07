using CouchLink.Core.Video;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace CouchLink.Video;

public readonly record struct PlayerOptions(nint NearWindow = 0, bool Windowed = false, bool PreferHardware = true);

/// <summary>
/// The client's video window. Its own thread owns the window, the D3D11 device, the decoder and the
/// presenter, and runs <see cref="PlayerCore"/> whenever a frame arrives or a window message does.
/// <see cref="Enqueue"/> is called from the receive thread. <c>closeRequested</c> runs on the player
/// thread: post it to the UI thread, and never Dispose the player from inside it.
/// </summary>
public sealed class VideoPlayer : IDisposable
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(50);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;
    private Exception? _startError;
    private PlayerCore? _core;

    public VideoPlayer(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,
        Action closeRequested, Action<string>? log = null, Func<string?>? audioLine = null)
    {
        _thread = new Thread(() => Run(options, stats, decodeFailed, closeRequested, log, audioLine))
        {
            IsBackground = true,
            Name = "CouchLink player",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _ready.Wait();
        if (_startError is { } e)
        {
            _thread.Join();
            throw new InvalidOperationException($"The video window could not start: {e.Message}", e);
        }
    }

    public nint WindowHandle { get; private set; }
    public string? HardwareDecodeError { get; private set; }
    public string DecoderName => _core?.DecoderName ?? "";
    public long FramesShown => _core?.FramesShown ?? 0;
    public TimeSpan ClientDelay => _core?.ClientDelay ?? TimeSpan.Zero;

    public void Enqueue(AssembledFrame frame) => _core!.Enqueue(frame);

    private void Run(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,
        Action closeRequested, Action<string>? log, Func<string?>? audioLine)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        PlayerWindow? window = null;
        FramePresenter? presenter = null;
        PlayerCore? core = null;
        TimerResolution? timer = null;
        try
        {
            if (!FfmpegLibrary.TryLoad(out var error))
                throw new InvalidOperationException(error);
            D3D11CreateDevice(null, DriverType.Hardware,
                DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out device, out context).CheckError();
            using (var multithread = device!.QueryInterface<ID3D11Multithread>())
                multithread.SetMultithreadProtected(true); // FFmpeg's D3D11VA locks the context too

            window = new PlayerWindow(options.NearWindow, options.Windowed);
            presenter = new FramePresenter(device, context!, window.Handle, window.Width, window.Height);
            var d = device;
            core = new PlayerCore(
                hardware => hardware && options.PreferHardware ? OpenHardware(d, log) : H264Decoder.OpenSoftware(),
                presenter, stats, decodeFailed, TimeProvider.System, log, audioLine);
            var c = core;
            var p = presenter;
            window.StatsToggled += () => c.ShowStats = !c.ShowStats;
            window.CloseRequested += closeRequested;
            window.Resized += p.Resize;
            timer = new TimerResolution(); // precise wakeups for the wait below
            WindowHandle = window.Handle;
            _core = core;
        }
        catch (Exception e)
        {
            _startError = e;
            core?.Dispose();
            presenter?.Dispose();
            window?.Dispose();
            context?.Dispose();
            device?.Dispose();
            _ready.Set();
            return;
        }
        _ready.Set();

        try
        {
            while (!_stop && window.PumpMessages())
            {
                core.Run();
                window.WaitForInput(core.FrameReady, IdleWait);
            }
        }
        catch (Exception e)
        {
            log?.Invoke($"Video player error: {e}");
            closeRequested();
        }
        finally
        {
            timer.Dispose();
            core.Dispose();
            presenter.Dispose();
            window.Dispose();
            context!.Dispose();
            device!.Dispose();
        }
    }

    private H264Decoder OpenHardware(ID3D11Device device, Action<string>? log)
    {
        var decoder = H264Decoder.Open(device, out var error);
        if (error is not null)
        {
            HardwareDecodeError = error;
            log?.Invoke($"Hardware video decoding unavailable ({error}); using software decoding");
        }
        return decoder;
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(); // the loop wakes within IdleWait
        _ready.Dispose();
    }
}
