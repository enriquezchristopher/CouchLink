using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

public readonly record struct VideoClientStats(
    VideoReceiveStats Receive,
    long FramesDelivered,
    long FramesSkipped,
    long KeyframeRequests,
    bool WaitingForKeyframe,
    bool HostPaused,
    TimeSpan? RoundTrip = null,
    TimeSpan HostDelay = default);

/// <summary>
/// Client side: receives shard datagrams, assembles and repairs frames, and passes decodable
/// frames to <c>onFrame</c> on the receive thread. Whenever the decode gate waits for a
/// keyframe it asks the host through <c>requestKeyframe</c>, at once and then every
/// <see cref="DecodeGate.RequestInterval"/>. A timer gives up stalled frames every
/// <see cref="TickInterval"/>. With <c>sendTimingPing</c> it pings the host every
/// <see cref="PingInterval"/> and keeps the round trip and the host's delay from the replies.
/// Given a receiver, it runs the receive loop and takes ownership of it; otherwise a
/// <see cref="StreamDispatcher"/> calls <see cref="Receive"/>.
/// </summary>
public sealed class VideoClient : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);

    private readonly VideoReceiver? _receiver;
    private readonly Action _requestKeyframe;
    private readonly Action<AssembledFrame> _onFrame;
    private readonly Action<Exception>? _onError;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly DecodeGate _gate = new();
    private readonly FrameAssembler _assembler;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _loop;
    private readonly ITimer _timer;
    private long _delivered;
    private bool _hostPaused;
    private readonly Action<long>? _sendTimingPing;
    private TimeSpan? _lastPing;
    private TimeSpan? _roundTrip;
    private TimeSpan _hostDelay;

    /// <summary>Datagrams come in through <see cref="Receive"/>, from a <see cref="StreamDispatcher"/>.</summary>
    public VideoClient(
        Action requestKeyframe,
        Action<AssembledFrame> onFrame,
        TimeProvider time,
        Action<Exception>? onError = null,
        Action<long>? sendTimingPing = null)
    {
        _sendTimingPing = sendTimingPing;
        _requestKeyframe = requestKeyframe;
        _onFrame = onFrame;
        _onError = onError;
        _time = time;
        _start = time.GetTimestamp();
        _assembler = new FrameAssembler(_gate.FrameLost);
        _timer = time.CreateTimer(_ => Tick(), null, TickInterval, TickInterval);
    }

    /// <summary>Runs <paramref name="receiver"/>'s receive loop itself, and owns it.</summary>
    public VideoClient(
        VideoReceiver receiver,
        Action requestKeyframe,
        Action<AssembledFrame> onFrame,
        TimeProvider time,
        Action<Exception>? onError = null,
        Action<long>? sendTimingPing = null)
        : this(requestKeyframe, onFrame, time, onError, sendTimingPing)
    {
        _receiver = receiver;
        _loop = receiver.RunAsync(Receive, _cts.Token, onError);
    }

    public VideoClientStats Stats
    {
        get
        {
            lock (_lock)
                return new VideoClientStats(
                    _assembler.Stats,
                    Interlocked.Read(ref _delivered),
                    _gate.FramesSkipped,
                    _gate.KeyframeRequests,
                    _gate.WaitingForKeyframe,
                    _hostPaused,
                    _roundTrip,
                    _hostDelay);
        }
    }

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>A video datagram: a shard or a timing reply. Call from one receive thread only.</summary>
    public void Receive(byte[] datagram)
    {
        if (TimingReply.TryParse(datagram, out var reply))
        {
            var rtt = Now - TimeSpan.FromTicks(reply.ClientTicks);
            lock (_lock)
            {
                _roundTrip = _roundTrip is { } old ? old + (rtt - old) / 4 : rtt;
                _hostDelay = reply.HostDelay;
            }
            return;
        }

        AssembledFrame? frame;
        lock (_lock)
        {
            frame = _assembler.Add(datagram, Now);
            if (frame is not null)
                _hostPaused = frame.Paused; // before the gate, so a skipped frame still counts
            if (frame is not null && !_gate.Accept(frame))
                frame = null;
        }
        if (frame is not null)
        {
            Interlocked.Increment(ref _delivered);
            _onFrame(frame);
        }
        RequestKeyframeIfNeeded(); // a loss found by this packet is reported at once
    }

    private void Tick()
    {
        try
        {
            lock (_lock)
                _assembler.AbandonStale(Now);
            RequestKeyframeIfNeeded();
            SendPingIfDue();
        }
        catch (Exception e)
        {
            _onError?.Invoke(e); // an exception on a timer thread would kill the process
        }
    }

    private void SendPingIfDue()
    {
        if (_sendTimingPing is null)
            return;
        var now = Now;
        lock (_lock)
        {
            if (_lastPing is { } last && now - last < PingInterval)
                return;
            _lastPing = now;
        }
        _sendTimingPing(now.Ticks);
    }

    /// <summary>The decoder failed; skip to the next keyframe and ask for it now. Any thread.</summary>
    public void DecodeFailed()
    {
        lock (_lock)
            _gate.DecodeFailed();
        RequestKeyframeIfNeeded();
    }

    private void RequestKeyframeIfNeeded()
    {
        bool send;
        lock (_lock)
            send = _gate.ShouldRequestKeyframe(Now);
        if (send)
            _requestKeyframe();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cts.Cancel();
        _loop?.Wait(TimeSpan.FromSeconds(2));
        _receiver?.Dispose();
        _cts.Dispose();
    }
}
