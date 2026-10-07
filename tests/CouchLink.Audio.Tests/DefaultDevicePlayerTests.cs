using System.Runtime.InteropServices;

namespace CouchLink.Audio.Tests;

public class DefaultDevicePlayerTests
{
    [Fact]
    public void No_audio_system_is_a_status_not_an_exception()
    {
        // e.g. the Windows Audio service is stopped: the join must still go ahead (spec: audio never fails a join).
        using var player = new DefaultDevicePlayer(_ => 0, null, () => throw new COMException("Audio service stopped"));

        Assert.Equal("unavailable: Audio service stopped", player.Status);
    }
}
