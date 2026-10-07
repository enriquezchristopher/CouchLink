using System.Net;
using CouchLink.Core.Net;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: the host's picture and sound, both arriving on UDP 47802 through one
/// <see cref="StreamDispatcher"/>. Video failing to start fails the join; audio never does.
/// Dispose before the <see cref="InputSender"/> that video sends through.
/// </summary>
internal sealed class ClientStreams : IDisposable
{
    private readonly StreamDispatcher _dispatcher;
    private readonly ClientVideoService _video;
    private readonly ClientAudioService _audio;

    private ClientStreams(StreamDispatcher dispatcher, ClientVideoService video, ClientAudioService audio)
    {
        _dispatcher = dispatcher;
        _video = video;
        _audio = audio;
    }

    public static bool TryStart(IPAddress host, InputSender sender, PlayerOptions options, string? savePath,
        Action leave, out ClientStreams? streams, out string? error)
    {
        streams = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        var audio = new ClientAudioService(host);
        if (!ClientVideoService.TryStart(sender, options, savePath, leave, audio.Describe, out var video, out error))
        {
            audio.Dispose();
            receiver!.Dispose();
            return false;
        }
        var dispatcher = new StreamDispatcher(receiver!, video!.Receive, audio.Receive,
            e => AppServices.Log.Write($"Receive error: {e}"));
        streams = new ClientStreams(dispatcher, video, audio);
        return true;
    }

    public nint PlayerWindow => _video.PlayerWindow;

    public string Describe() => $"{_video.Describe()}\n{_audio.Describe()}";

    public void Dispose()
    {
        _dispatcher.Dispose(); // stops the receive thread before video and audio go
        _video.Dispose();
        _audio.Dispose();
    }
}
