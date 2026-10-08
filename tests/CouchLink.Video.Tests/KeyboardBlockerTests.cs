using System.Runtime.InteropServices;
using CouchLink.Video;

namespace CouchLink.Video.Tests;

public partial class KeyboardBlockerTests
{
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [Fact]
    public void The_hook_runs_on_its_own_thread_not_the_callers()
    {
        // Windows calls a low-level hook for every keystroke on the PC and silently removes one whose
        // thread is too slow to answer; the player thread decodes video, so the hook must not live there.
        using var blocker = new KeyboardBlocker();
        Assert.True(blocker.Install(out var error), error);
        Assert.NotEqual(0u, blocker.HookThreadId);
        Assert.NotEqual(GetCurrentThreadId(), blocker.HookThreadId);
    }

    [Fact]
    public void Uninstall_stops_the_hook_thread_and_it_can_be_installed_again()
    {
        using var blocker = new KeyboardBlocker();
        Assert.True(blocker.Install(out _));
        blocker.Uninstall();
        Assert.False(blocker.IsInstalled);
        Assert.Equal(0u, blocker.HookThreadId);

        Assert.True(blocker.Install(out _));
        Assert.True(blocker.IsInstalled);
    }
}
