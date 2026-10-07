using System.Runtime.InteropServices;

namespace CouchLink.Core.Audio;

/// <summary>
/// Cuts captured 16-bit stereo PCM, which arrives in buffers of any size, into exact 5 ms frames
/// and hands each to <c>onFrame</c> as a new array. A discontinuity throws away the partial frame
/// and marks the next whole frame, and so is the very first frame (nothing came before it).
/// Not thread-safe.
/// </summary>
public sealed class FrameSlicer(Action<short[], bool> onFrame)
{
    private readonly short[] _partial = new short[AudioFormat.FrameValues];
    private int _filled;
    private bool _discontinuity = true;

    /// <summary>Adds captured bytes. <paramref name="silent"/>: treat the buffer as silence (WASAPI's silent flag).</summary>
    public void Write(ReadOnlySpan<byte> pcm, bool discontinuity = false, bool silent = false)
    {
        if (discontinuity)
        {
            _filled = 0;
            _discontinuity = true;
        }

        var source = MemoryMarshal.Cast<byte, short>(pcm);
        int done = 0;
        while (done < source.Length)
        {
            int take = Math.Min(source.Length - done, _partial.Length - _filled);
            var target = _partial.AsSpan(_filled, take);
            if (silent)
                target.Clear();
            else
                source.Slice(done, take).CopyTo(target);
            _filled += take;
            done += take;

            if (_filled == _partial.Length)
            {
                onFrame(_partial.ToArray(), _discontinuity);
                _filled = 0;
                _discontinuity = false;
            }
        }
    }
}
