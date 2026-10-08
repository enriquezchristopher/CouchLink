using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class GpuPriorityPolicyTests
{
    [Fact]
    public void Amd_hosts_ask_for_realtime_then_high()
    {
        // Like Sunshine: the host's capture and encode jump the GPU queue ahead of the game, so a
        // game that keeps the GPU busy can't starve the stream (the RX 550 fell to ~26 fps).
        Assert.Equal([GpuSchedulingClass.Realtime, GpuSchedulingClass.High],
            GpuPriorityPolicy.ClassesToTry(EncoderChoice.AmdVendorId));
    }

    [Fact]
    public void Nvidia_hosts_ask_only_for_high()
    {
        // Sunshine avoids realtime on NVIDIA: with hardware-accelerated GPU scheduling it can misbehave.
        Assert.Equal([GpuSchedulingClass.High], GpuPriorityPolicy.ClassesToTry(EncoderChoice.NvidiaVendorId));
    }

    [Fact]
    public void Other_GPUs_ask_for_realtime_then_high()
    {
        Assert.Equal([GpuSchedulingClass.Realtime, GpuSchedulingClass.High], GpuPriorityPolicy.ClassesToTry(0x8086));
    }

    [Fact]
    public void The_device_thread_priority_is_the_highest_DXGI_allows()
    {
        Assert.Equal(7, GpuPriorityPolicy.DeviceThreadPriority);
    }

    [Theory]
    [InlineData(GpuSchedulingClass.Realtime, true, "GPU priority: realtime, capture device raised")]
    [InlineData(GpuSchedulingClass.High, false, "GPU priority: high, capture device normal")]
    [InlineData(null, true, "GPU priority: normal (Windows refused a higher class), capture device raised")]
    public void The_result_is_described_for_the_log_and_Details(GpuSchedulingClass? granted, bool device, string text)
    {
        Assert.Equal(text, GpuPriorityPolicy.Describe(granted, device));
    }
}
