using System.Diagnostics;
using System.Runtime.InteropServices;
using CouchLink.Core.Video;
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>Applies <see cref="GpuPriorityPolicy"/> to this process and a capture's device. Host side only.</summary>
public static partial class GpuPriority
{
    /// <summary>Raises what Windows allows and says what it got, for the log and the host's Details.</summary>
    public static string Raise(DesktopCapture capture)
    {
        bool deviceRaised;
        using (var dxgi = capture.Device.QueryInterface<IDXGIDevice>())
            deviceRaised = dxgi.SetGPUThreadPriority(GpuPriorityPolicy.DeviceThreadPriority).Success;

        GpuSchedulingClass? granted = null;
        using var self = Process.GetCurrentProcess();
        foreach (var cls in GpuPriorityPolicy.ClassesToTry(capture.VendorId))
        {
            if (D3DKMTSetProcessSchedulingPriorityClass(self.Handle, (int)cls) == 0) // STATUS_SUCCESS
            {
                granted = cls;
                break;
            }
        }
        return GpuPriorityPolicy.Describe(granted, deviceRaised);
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTSetProcessSchedulingPriorityClass(nint process, int priorityClass);
}
