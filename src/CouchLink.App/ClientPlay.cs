using System.Net;
using System.Windows;
using System.Windows.Interop;
using CouchLink.App.Input;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side, once the host let us in: the host's picture and sound, and this PC's keyboard and
/// mouse driving the slot's pad. Input counts only while the app window or the player window is in
/// front; losing focus releases every key. Create and dispose on the UI thread.
/// </summary>
internal sealed class ClientPlay : IDisposable
{
    private readonly Window _window;
    private readonly ClientStreams _streams;
    private readonly InputMapper _mapper;
    private readonly RawInputSource _rawInput;
    private readonly ClientInputLoop _input;

    private ClientPlay(Window window, ClientStreams streams, InputSender sender)
    {
        _window = window;
        _streams = streams;
        _mapper = new InputMapper(AppServices.Controls);
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(window), InputAllowed);
        _rawInput.InputSuspended += _mapper.ReleaseAll;
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;
        _input = new ClientInputLoop(_mapper, sender);
        _window.Deactivated += OnDeactivated;
    }

    public static bool TryStart(Window window, IPAddress host, byte slot, Action leave, Func<string?> sessionStatus,
        Action openControls,
        out ClientPlay? play, out string? error)
    {
        play = null;
        var sender = new InputSender(new IPEndPoint(host, Ports.Input), slot);
        var options = new PlayerOptions(new WindowInteropHelper(window).Handle, AppServices.Options.WindowedPlayer,
            LockInput: !AppServices.Options.WindowedPlayer);
        if (!ClientStreams.TryStart(host, sender, options, AppServices.Options.SaveVideoPath, leave, sessionStatus,
                () => OverlayText.Controls(AppServices.Controls), openControls,
                out var streams, out error))
        {
            sender.Dispose();
            return false;
        }
        play = new ClientPlay(window, streams!, sender);
        return true;
    }

    public nint PlayerWindow => _streams.PlayerWindow;

    private bool InputAllowed()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == new WindowInteropHelper(_window).Handle || foreground == _streams.PlayerWindow;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!InputAllowed())
            _mapper.ReleaseAll(); // never leave keys stuck
    }

    /// <summary>The pad state being sent and the streams' stats, for the Details section.</summary>
    public string Describe()
    {
        var s = _input.LastSent;
        return $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}\n{_streams.Describe()}";
    }

    public void Dispose()
    {
        _window.Deactivated -= OnDeactivated;
        _streams.Dispose(); // before the input sender that video sends keyframe requests through
        _input.Dispose();   // sends a neutral pad state and closes the input sender
        _mapper.Dispose();  // stops following controls edits
        _rawInput.Dispose();
    }
}
