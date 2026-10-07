using System.Net;
using CouchLink.Audio;
using CouchLink.Core.Audio;
using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: plays the host's sound from the datagrams <see cref="Receive"/> is given. It never
/// fails to start. If the host is this same PC it does not play at all, because the host's capture
/// would pick up our playback and send it round again; it still receives and counts. With
/// --audio-loss=&lt;percent&gt; it drops that share of audio packets on purpose.
/// </summary>
internal sealed class ClientAudioService : IDisposable
{
    public const string SamePcMuted = "muted: host is this PC";

    private readonly AudioClient _client = new(new OpusAudioDecoder());
    private readonly DefaultDevicePlayer? _player;
    private readonly double _lossPercent = AppServices.Options.AudioLossPercent;

    public ClientAudioService(IPAddress host)
    {
        if (LocalAddress.IsThisPc(host))
            AppServices.Log.Write($"Audio: {SamePcMuted}");
        else
            _player = new DefaultDevicePlayer(_client.Read, message => AppServices.Log.Write(message));
    }

    /// <summary>An audio datagram, from the receive thread.</summary>
    public void Receive(byte[] datagram)
    {
        if (_lossPercent > 0 && Random.Shared.NextDouble() * 100 < _lossPercent)
            return;
        _client.Receive(datagram);
    }

    /// <summary>The audio line for the F2 overlay and the dev window.</summary>
    public string Describe() => OverlayText.Audio(_client.Stats, _player?.Status ?? SamePcMuted);

    public void Dispose()
    {
        _player?.Dispose(); // stops the device thread before the client goes
        _client.Dispose();
    }
}
