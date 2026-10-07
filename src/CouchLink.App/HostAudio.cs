using System.Net;
using CouchLink.Audio;
using CouchLink.Core.Audio;
using CouchLink.Core.Net;

namespace CouchLink.App;

/// <summary>
/// The host's sound: loopback capture (or, with --test-tone, a beep) encoded once and sent to every
/// client the session lets in. If audio can't start, hosting still runs video and the pads, and
/// <see cref="Describe"/> says why.
/// </summary>
internal sealed class HostAudio : IDisposable
{
    private readonly AudioStreamer? _streamer;
    private readonly string _summary;

    private HostAudio(AudioStreamer? streamer, string summary)
    {
        _streamer = streamer;
        _summary = summary;
    }

    public static HostAudio Start(Action<Exception> onError)
    {
        IAudioSource? source = null;
        try
        {
            source = AppServices.Options.TestTone
                ? new TestToneSource()
                : LoopbackSource.Open(message => AppServices.Log.Write(message));
            var streamer = new AudioStreamer(source, new OpusAudioEncoder(), new VideoSender(), Ports.Video, onError);
            var summary = $"Opus {AudioFormat.BitRate / 1000} kbps from {source.Description}";
            AppServices.Log.Write($"Audio: {summary}");
            return new HostAudio(streamer, summary);
        }
        catch (Exception e)
        {
            source?.Dispose();
            AppServices.Log.Write($"Audio unavailable: {e}");
            return new HostAudio(null, $"unavailable: {e.Message}");
        }
    }

    public void AddTarget(byte slot, IPAddress address) => _streamer?.AddTarget(slot, address);

    public void RemoveTarget(byte slot) => _streamer?.RemoveTarget(slot);

    public string Describe()
    {
        var text = $"Audio: {_summary}";
        if (_streamer is { } streamer)
        {
            var s = streamer.Stats;
            text += $"\n  {s.Clients} client(s), {s.PacketsSent} packets, {s.BytesSent / 1_000_000.0:0.0} MB";
        }
        return text;
    }

    public void Dispose() => _streamer?.Dispose(); // the streamer owns the source, encoder and sender
}
