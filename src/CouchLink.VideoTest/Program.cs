using System.Diagnostics;
using CouchLink.Video;

// VideoTest: checks host capture and encoding on this PC's GPU without a client.
//   VideoTest capture [seconds]   counts screen changes and lost captures
if (args.Length == 0 || args[0] is not ("capture" or "encode"))
{
    Console.WriteLine("Usage: VideoTest capture [seconds] | VideoTest encode [seconds] [--resolution=1080p] [--fps=60] [--encoder=h264_amf] [--out=videotest.h264]");
    return 2;
}

int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 5;
return args[0] == "capture" ? Capture(seconds) : 2;

static int Capture(int seconds)
{
    using var capture = DesktopCapture.Open();
    Console.WriteLine($"Adapter: {capture.AdapterName} (vendor 0x{capture.VendorId:X4})");
    Console.WriteLine($"Screen: {capture.Width}x{capture.Height} at {capture.RefreshRate} Hz (DisplayInfo says {DisplayInfo.PrimaryRefreshRate()} Hz)");

    int changed = 0, unchanged = 0, lost = 0;
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        switch (capture.TryCapture(TimeSpan.FromMilliseconds(16)))
        {
            case CaptureStatus.NewFrame: changed++; break;
            case CaptureStatus.NoChange: unchanged++; break;
            default: lost++; Thread.Sleep(16); break;
        }
    }
    Console.WriteLine($"{seconds} s: {changed} screen changes, {unchanged} waits with no change, {lost} lost");
    bool ok = changed > 0 && lost == 0;
    Console.WriteLine(ok ? "PASS: capture works" : "FAIL: no screen changes captured, or capture was lost");
    return ok ? 0 : 1;
}
