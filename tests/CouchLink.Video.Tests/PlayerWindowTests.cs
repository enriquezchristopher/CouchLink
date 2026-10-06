using System.Runtime.InteropServices;
using CouchLink.Video;

namespace CouchLink.Video.Tests;

public partial class PlayerWindowTests
{
    private const uint WM_SIZE = 0x0005;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [Fact]
    public void An_error_in_a_window_handler_comes_out_of_PumpMessages()
    {
        // An exception escaping the native window procedure would end the process with no crash
        // report; it must reach the player loop instead, which reports it.
        using var window = new PlayerWindow(0, windowed: true);
        window.Resized += (_, _) => throw new InvalidOperationException("resize failed");

        SendMessage(window.Handle, WM_SIZE, 0, (480 << 16) | 640);

        var e = Assert.Throws<InvalidOperationException>(() => window.PumpMessages());
        Assert.Equal("resize failed", e.Message);
        Assert.True(window.PumpMessages()); // reported once; the window still works
    }
}
