using System.IO;
using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: receives the host's video on UDP 47802. Until Plan 5 adds a decoder it can
/// save the H.264 it receives (--save-video=&lt;file&gt;) so the stream can be checked with
/// ffprobe/ffplay. Dispose before the <see cref="InputSender"/> it sends keyframe requests through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private readonly FileStream? _save;

    private ClientVideoService(VideoReceiver receiver, InputSender sender, string? savePath)
    {
        _save = savePath is null ? null : File.Create(savePath);
        _client = new VideoClient(
            receiver, sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"), sender.SendTimingPing);
    }

    public static bool TryStart(InputSender sender, string? savePath, out ClientVideoService? service, out string? error)
    {
        service = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        service = new ClientVideoService(receiver!, sender, savePath);
        return true;
    }

    private void OnFrame(AssembledFrame frame) => _save?.Write(frame.Data); // receive thread only

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {s.FramesDelivered} frames, {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "") +
               (s.HostPaused ? ", host screen paused" : "") +
               (_save is null ? "" : $"\nSaving to {_save.Name}");
    }

    public void Dispose()
    {
        _client.Dispose(); // stops the receive thread before the file closes
        _save?.Dispose();
    }
}
