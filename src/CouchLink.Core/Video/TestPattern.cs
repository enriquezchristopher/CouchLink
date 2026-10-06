using System.Buffers.Binary;
using System.Diagnostics;

namespace CouchLink.Core.Video;

/// <summary>
/// Fake encoded frames for testing the stream without a GPU. The bytes are deterministic
/// (sequence number, keyframe flag, length, then seeded pseudo-random data), so a client can
/// check every byte and any corruption in the pipeline shows up.
/// </summary>
public static class TestPattern
{
    public const int KeyframeBytes = 150_000;
    public const int DeltaFrameBytes = 20_000;
    private const int HeaderBytes = 13; // u64 sequence, u8 keyframe, i32 length

    public static byte[] Create(ulong sequence, bool keyframe)
    {
        int length = keyframe ? KeyframeBytes : DeltaFrameBytes + (int)(sequence % 5_000);
        var data = new byte[length];
        BinaryPrimitives.WriteUInt64LittleEndian(data, sequence);
        data[8] = keyframe ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(9), length);
        new Random(unchecked((int)sequence)).NextBytes(data.AsSpan(HeaderBytes));
        return data;
    }

    public static bool Verify(ReadOnlySpan<byte> frame, bool keyframe)
    {
        if (frame.Length < HeaderBytes)
            return false;
        var expected = Create(BinaryPrimitives.ReadUInt64LittleEndian(frame), keyframe);
        return frame.SequenceEqual(expected);
    }
}

/// <summary>An <see cref="IEncodedVideoSource"/> that produces test-pattern frames at a fixed rate.</summary>
public sealed class TestPatternSource(TimeSpan frameInterval) : IEncodedVideoSource
{
    public static readonly TimeSpan SixtyFps = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _next;
    private ulong _sequence;

    public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
    {
        var now = _clock.Elapsed;
        if (_next > now)
        {
            var wait = _next - now;
            if (wait > timeout)
            {
                Thread.Sleep(timeout);
                frame = default;
                return false;
            }
            Thread.Sleep(wait);
            now = _next;
        }
        _next = now + frameInterval;

        bool keyframe = forceKeyframe || _sequence == 0;
        frame = new EncodedFrame(TestPattern.Create(_sequence++, keyframe), keyframe);
        return true;
    }

    public void Dispose()
    {
    }
}
