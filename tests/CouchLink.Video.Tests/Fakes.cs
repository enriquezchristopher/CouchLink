using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

/// <summary>Plays back scripted capture results; NoChange waits the whole timeout, NewFrame and Lost return at once.</summary>
internal sealed class FakeCapture(FakeTimeProvider time) : IScreenCapture
{
    public Queue<CaptureStatus> Script { get; } = new();
    public List<TimeSpan> Waits { get; } = [];
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public TimeSpan Overshoot { get; set; } // a real wait returns a little late
    public bool Disposed { get; private set; }

    public CaptureStatus TryCapture(TimeSpan timeout)
    {
        Waits.Add(timeout);
        var status = Script.Count > 0 ? Script.Dequeue() : CaptureStatus.NoChange;
        if (status == CaptureStatus.NoChange)
            time.Advance(timeout + Overshoot);
        return status;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Records what it was asked to do; its first frame is a keyframe, like a real encoder's.</summary>
internal sealed class FakeEncoder(string name, VideoSize size) : IFrameEncoder
{
    private bool _first = true;

    public string Name { get; } = name;
    public bool IsHardware { get; } = EncoderChoice.IsHardware(name);
    public VideoSize Size { get; } = size;
    public List<bool> ForcedKeyframes { get; } = [];
    public bool ProducePackets { get; set; } = true;
    public Action? OnEncode { get; set; } // e.g. advance fake time to simulate encoding work
    public bool Disposed { get; private set; }

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        ForcedKeyframes.Add(forceKeyframe);
        OnEncode?.Invoke();
        bool keyframe = forceKeyframe || _first;
        _first = false;
        frame = new EncodedFrame(new byte[] { 1, 2, 3 }, keyframe);
        return ProducePackets;
    }

    public void Dispose() => Disposed = true;
}
