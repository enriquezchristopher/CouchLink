using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Video;

public readonly record struct VideoSendStats(long FramesSent, long KeyframesSent, long BytesSent, int Clients);

/// <summary>
/// Host side: on its own thread, pulls encoded frames from the source, packetizes each frame
/// once and sends the packets to every current client. Clients are learned from their input
/// packets (<see cref="ClientSeen"/>); a new client or a keyframe request forces a keyframe.
/// Takes ownership of the source and the sender.
/// </summary>
public sealed class VideoStreamer : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(50);

    private readonly IEncodedVideoSource _source;
    private readonly IVideoPacketSender _sender;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly Action<Exception>? _onError;
    private readonly FramePacketizer _packetizer;
    private readonly StreamTargets _targets;
    private readonly KeyframePolicy _keyframes = new();
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private uint _frameNumber;
    private long _framesSent, _keyframesSent, _bytesSent;
    private int _clients;

    public VideoStreamer(
        IEncodedVideoSource source,
        IVideoPacketSender sender,
        int videoPort,
        TimeProvider time,
        Action<Exception>? onError = null,
        int parityPercent = FramePacketizer.DefaultParityPercent)
    {
        _source = source;
        _sender = sender;
        _time = time;
        _start = time.GetTimestamp();
        _onError = onError;
        // Random per stream, so clients can tell a restarted stream from late packets.
        _packetizer = new FramePacketizer((ushort)Random.Shared.Next(1, ushort.MaxValue + 1), parityPercent);
        _targets = new StreamTargets(videoPort);
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink video", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public VideoSendStats Stats => new(
        Interlocked.Read(ref _framesSent),
        Interlocked.Read(ref _keyframesSent),
        Interlocked.Read(ref _bytesSent),
        Volatile.Read(ref _clients));

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>A client's input packet arrived; it gets video, and a keyframe if it is new.</summary>
    public void ClientSeen(byte slot, IPAddress address)
    {
        lock (_gate)
            if (_targets.Seen(slot, address, Now))
                _keyframes.Request();
    }

    public void RequestKeyframe()
    {
        lock (_gate)
            _keyframes.Request();
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
                _onError?.Invoke(e);
                Thread.Sleep(100); // don't spin if the source keeps failing
            }
        }
    }

    private void StreamOneFrame()
    {
        bool force;
        lock (_gate)
            force = _keyframes.ShouldForce(Now);
        if (!_source.TryGetFrame(force, PollTimeout, out var frame))
            return;

        IReadOnlyList<IPEndPoint> targets;
        lock (_gate)
        {
            if (frame.Keyframe)
                _keyframes.KeyframeSent(Now);
            targets = _targets.Current(Now);
        }
        Volatile.Write(ref _clients, targets.Count);
        if (targets.Count == 0)
            return; // nobody to send to; frame numbers stay consecutive for the next client

        var packets = _packetizer.Packetize(_frameNumber++, frame.Data.Span, frame.Keyframe, frame.Paused);
        _sender.Send(packets, targets);
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _bytesSent, frame.Data.Length);
        if (frame.Keyframe)
            Interlocked.Increment(ref _keyframesSent);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _source.Dispose();
        _sender.Dispose();
    }
}
