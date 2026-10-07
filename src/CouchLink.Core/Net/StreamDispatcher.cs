using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// Client side: the one socket on the video port (47802) carries video shards, timing replies and
/// audio. This runs its receive loop and hands each datagram to the video or the audio handler by
/// its packet type; anything else is ignored. An exception in a handler goes to <c>onError</c> and
/// the loop carries on, so a failing audio handler never stops video. Takes ownership of the receiver.
/// </summary>
public sealed class StreamDispatcher : IDisposable
{
    private readonly VideoReceiver _receiver;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public StreamDispatcher(VideoReceiver receiver, Action<byte[]> video, Action<byte[]> audio, Action<Exception>? onError = null)
    {
        _receiver = receiver;
        _loop = receiver.RunAsync(datagram => Route(datagram, video, audio), _cts.Token, onError);
    }

    public static void Route(byte[] datagram, Action<byte[]> video, Action<byte[]> audio)
    {
        if (!Wire.TryGetType(datagram, out var type))
            return;
        if (type == Wire.TypeAudio)
            audio(datagram);
        else if (type is Wire.TypeVideoShard or Wire.TypeTimingReply)
            video(datagram);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loop.Wait(TimeSpan.FromSeconds(2));
        _receiver.Dispose();
        _cts.Dispose();
    }
}
