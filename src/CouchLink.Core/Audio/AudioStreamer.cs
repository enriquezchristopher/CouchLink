using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Audio;

/// <summary>Encodes one 5 ms frame of interleaved 16-bit stereo PCM.</summary>
public interface IAudioEncoder : IDisposable
{
    /// <summary>Returns the number of bytes written to <paramref name="output"/>.</summary>
    int Encode(ReadOnlySpan<short> pcm, Span<byte> output);
}

/// <summary>A packet goes to every client; <see cref="PacketsSent"/> and <see cref="BytesSent"/> count it once.</summary>
public readonly record struct AudioSendStats(long FramesCaptured, long PacketsSent, long BytesSent, int Clients);

/// <summary>
/// Host side: on its own thread, reads 5 ms frames from the source, encodes each once and sends it,
/// with the previous frame attached, to every current client. Clients are added and removed by the
/// session (<see cref="AddTarget"/>), as for video. After a discontinuity the packet carries no
/// previous frame and the sequence jumps by <see cref="SequenceJump"/>, so a client starts over
/// instead of treating the new audio as late. While no client listens nothing is encoded or sent.
/// Takes ownership of the source, the encoder and the sender.
/// </summary>
public sealed class AudioStreamer : IDisposable
{
    public const uint SequenceJump = 16;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(50);

    private readonly IAudioSource _source;
    private readonly IAudioEncoder _encoder;
    private readonly IVideoPacketSender _sender;
    private readonly Action<Exception>? _onError;
    private readonly StreamTargets _targets;
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private readonly short[] _pcm = new short[AudioFormat.FrameValues];
    private readonly byte[] _encoded = new byte[AudioPacket.MaxFrameBytes];
    private volatile bool _running = true;
    private byte[]? _previous;
    private uint _sequence;
    private long _framesCaptured, _packetsSent, _bytesSent;
    private int _clients;

    public AudioStreamer(
        IAudioSource source,
        IAudioEncoder encoder,
        IVideoPacketSender sender,
        int port,
        Action<Exception>? onError = null)
    {
        _source = source;
        _encoder = encoder;
        _sender = sender;
        _onError = onError;
        _targets = new StreamTargets(port);
        // Random per stream, so clients can tell a restarted host from late packets.
        StreamId = (ushort)Random.Shared.Next(1, ushort.MaxValue + 1);
        // Highest: a late frame is an audible gap, and the work is 0.3 ms per 5 ms.
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink audio", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public ushort StreamId { get; }

    public string SourceDescription => _source.Description;

    public AudioSendStats Stats => new(
        Interlocked.Read(ref _framesCaptured),
        Interlocked.Read(ref _packetsSent),
        Interlocked.Read(ref _bytesSent),
        Volatile.Read(ref _clients));

    /// <summary>The session let a client in: it gets audio from the next frame.</summary>
    public void AddTarget(byte slot, IPAddress address)
    {
        lock (_gate)
            _targets.Add(slot, address);
    }

    /// <summary>The client left, was kicked or went silent: no more audio for it.</summary>
    public void RemoveTarget(byte slot)
    {
        lock (_gate)
            _targets.Remove(slot);
    }

    private void Run()
    {
        while (_running)
        {
            try
            {
                StreamOneFrame();
            }
            catch (Exception e)
            {
                _previous = null; // the next packet must not claim to follow a frame that never went out
                _onError?.Invoke(e);
                Thread.Sleep(100); // don't spin if the source or encoder keeps failing
            }
        }
    }

    private void StreamOneFrame()
    {
        if (!_source.TryRead(_pcm, PollTimeout, out bool discontinuity))
            return;
        Interlocked.Increment(ref _framesCaptured);
        if (discontinuity)
        {
            _previous = null;
            _sequence += SequenceJump;
        }

        IReadOnlyList<IPEndPoint> targets;
        lock (_gate)
            targets = _targets.Current();
        Volatile.Write(ref _clients, targets.Count);
        if (targets.Count == 0)
        {
            _previous = null; // whoever joins next starts cleanly
            return;
        }

        int length = _encoder.Encode(_pcm, _encoded);
        var frame = _encoded.AsSpan(0, length).ToArray();
        var packet = new AudioPacket(StreamId, _sequence++, frame, _previous).ToArray();
        _previous = frame;
        _sender.Send([packet], targets);
        Interlocked.Increment(ref _packetsSent);
        Interlocked.Add(ref _bytesSent, packet.Length);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _source.Dispose();
        _encoder.Dispose();
        _sender.Dispose();
    }
}
