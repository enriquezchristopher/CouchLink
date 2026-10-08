namespace CouchLink.Core.Video;

/// <summary>Windows' GPU scheduling classes for a process (D3DKMT_SCHEDULINGPRIORITYCLASS).</summary>
public enum GpuSchedulingClass
{
    High = 4,
    Realtime = 5,
}

/// <summary>
/// How the host's capture and encode get ahead of the game on the GPU, as Sunshine does: the
/// capture device's GPU thread priority goes to the maximum and the process's GPU scheduling class
/// to realtime, or high if Windows refuses. A game that keeps the GPU busy otherwise starves the
/// stream (an RX 550 under NBA 2K22 sent ~26 fps). NVIDIA gets high only: Sunshine avoids realtime
/// there because it can misbehave with hardware-accelerated GPU scheduling.
/// </summary>
public static class GpuPriorityPolicy
{
    /// <summary>The highest value IDXGIDevice::SetGPUThreadPriority accepts.</summary>
    public const int DeviceThreadPriority = 7;

    public static IReadOnlyList<GpuSchedulingClass> ClassesToTry(uint vendorId) =>
        vendorId == EncoderChoice.NvidiaVendorId
            ? [GpuSchedulingClass.High]
            : [GpuSchedulingClass.Realtime, GpuSchedulingClass.High];

    public static string Describe(GpuSchedulingClass? granted, bool deviceRaised) =>
        $"GPU priority: {(granted is { } g ? g.ToString().ToLowerInvariant() : "normal (Windows refused a higher class)")}, " +
        $"capture device {(deviceRaised ? "raised" : "normal")}";
}
