using System.IO;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: shows the host's video, from the datagrams <see cref="Receive"/> is given, in the
/// player window (and, with --save-video=&lt;file&gt;, also saves the H.264). <c>leave</c> runs on the
/// player thread when the player asks to leave (Ctrl+Alt+Q, Alt+F4). <c>audioLine</c> is the F2
/// overlay's audio line. Dispose before the <see cref="InputSender"/> it sends keyframe requests
/// and timing pings through. <c>sessionStatus</c> replaces the picture status while it returns text ("Reconnecting...").
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private readonly VideoPlayer _player;
    private readonly FileStream? _save;

    private ClientVideoService(InputSender sender, PlayerOptions options, string? savePath, Action leave, Func<string?> audioLine,
        Func<string?> sessionStatus, Func<string?> controlsText, Action openControls)
    {
        VideoClient? client = null;
        _player = new VideoPlayer(options, () => client?.Stats ?? default, () => client?.DecodeFailed(),
            leave, message => AppServices.Log.Write(message), audioLine, sessionStatus, controlsText, openControls);
        _save = savePath is null ? null : File.Create(savePath);
        client = new VideoClient(
            sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"), sender.SendTimingPing);
        _client = client;
    }

    public static bool TryStart(InputSender sender, PlayerOptions options, string? savePath, Action leave,
        Func<string?> audioLine, Func<string?> sessionStatus, Func<string?> controlsText, Action openControls,
        out ClientVideoService? service, out string? error)
    {
        try
        {
            service = new ClientVideoService(sender, options, savePath, leave, audioLine, sessionStatus, controlsText, openControls);
            error = null;
            return true;
        }
        catch (InvalidOperationException e)
        {
            service = null;
            error = e.Message;
            return false;
        }
    }

    public nint PlayerWindow => _player.WindowHandle;

    /// <summary>A video datagram, from the receive thread.</summary>
    public void Receive(byte[] datagram) => _client.Receive(datagram);

    private void OnFrame(AssembledFrame frame) // receive thread only
    {
        _save?.Write(frame.Data);
        _player.Enqueue(frame);
    }

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {_player.FramesShown} shown ({_player.DecoderName}), {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "") +
               (s.HostPaused ? ", host screen paused" : "") +
               (s.RoundTrip is { } rtt ? $", round trip {rtt.TotalMilliseconds:0.0} ms" : "") +
               (_player.InputLockError is { } lockError ? $"\n{lockError}" : "") +
               (_save is null ? "" : $"\nSaving to {_save.Name}");
    }

    public void Dispose()
    {
        _client.Dispose(); // stops the receive thread before the player and the file close
        _player.Dispose();
        _save?.Dispose();
    }
}
