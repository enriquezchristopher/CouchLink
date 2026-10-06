using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: receives the host's video on UDP 47802 and, until Plan 4 adds a decoder,
/// checks every frame against the test pattern so corruption shows in the dev window.
/// Dispose before the <see cref="InputSender"/> it sends keyframe requests through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private long _corrupt;

    private ClientVideoService(VideoReceiver receiver, InputSender sender)
    {
        _client = new VideoClient(
            receiver, sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"));
    }

    public static bool TryStart(InputSender sender, out ClientVideoService? service, out string? error)
    {
        service = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        service = new ClientVideoService(receiver!, sender);
        return true;
    }

    private void OnFrame(AssembledFrame frame)
    {
        if (!TestPattern.Verify(frame.Data, frame.Keyframe))
            Interlocked.Increment(ref _corrupt);
    }

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {s.FramesDelivered} frames, {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, {Interlocked.Read(ref _corrupt)} corrupt, " +
               $"loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "");
    }

    public void Dispose() => _client.Dispose();
}
