using System.Runtime.InteropServices;
using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class FrameSlicerTests
{
    /// <summary>Little-endian bytes of the 16-bit values first, first + 1, ...</summary>
    private static byte[] Bytes(int first, int count) =>
        MemoryMarshal.AsBytes(Enumerable.Range(first, count).Select(v => (short)v).ToArray().AsSpan()).ToArray();

    [Fact]
    public void Uneven_buffers_become_exact_frames_with_nothing_lost_or_repeated()
    {
        var frames = new List<short[]>();
        var slicer = new FrameSlicer((f, _) => frames.Add(f));

        int next = 0;
        foreach (int values in new[] { 960, 2, 1000, 3, 480, 1395 }) // 3840 values = 8 frames
        {
            slicer.Write(Bytes(next, values));
            next += values;
        }

        Assert.Equal(8, frames.Count);
        Assert.All(frames, f => Assert.Equal(AudioFormat.FrameValues, f.Length));
        Assert.Equal(Enumerable.Range(0, 3840).Select(v => (short)v), frames.SelectMany(f => f));
    }

    [Fact]
    public void A_discontinuity_drops_the_partial_frame_and_marks_the_next()
    {
        var frames = new List<(short[] Frame, bool Discontinuity)>();
        var slicer = new FrameSlicer((f, d) => frames.Add((f, d)));

        slicer.Write(Bytes(0, 960));                       // two frames; the first follows nothing
        slicer.Write(Bytes(5000, 100));                    // part of a frame...
        slicer.Write(Bytes(0, 480), discontinuity: true);  // ...thrown away here
        slicer.Write(Bytes(0, 480));

        Assert.Equal(new[] { true, false, true, false }, frames.Select(f => f.Discontinuity));
        Assert.Equal(Enumerable.Range(0, 480).Select(v => (short)v), frames[2].Frame);
    }

    [Fact]
    public void A_silent_buffer_becomes_zeros()
    {
        var frames = new List<short[]>();
        var slicer = new FrameSlicer((f, _) => frames.Add(f));

        slicer.Write(Bytes(1, 480), silent: true);

        Assert.All(Assert.Single(frames), v => Assert.Equal(0, v));
    }
}
