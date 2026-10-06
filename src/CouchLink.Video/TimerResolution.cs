using System.Runtime.InteropServices;

namespace CouchLink.Video;

/// <summary>
/// Raises Windows' timer resolution to 1 ms while alive. With the default ~15.6 ms tick,
/// Thread.Sleep for the few milliseconds left in a frame slot oversleeps a whole tick, and a
/// 60 fps stream comes out at ~33 fps.
/// </summary>
internal sealed partial class TimerResolution : IDisposable
{
    private bool _active = timeBeginPeriod(1) == 0;

    public void Dispose()
    {
        if (_active)
            timeEndPeriod(1);
        _active = false;
    }

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);
}
