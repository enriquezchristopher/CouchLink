using CouchLink.Video;

namespace CouchLink.Video.Tests;

public class H264FramesTests
{
    [Fact]
    public void A_recorded_stream_splits_back_into_its_frames()
    {
        var packets = TestStreams.X264(160, 120, frames: 12);
        var file = packets.SelectMany(p => p).ToArray();

        var frames = H264Frames.Split(file);

        Assert.Equal(packets.Count, frames.Count);
        Assert.Equal(packets[0], frames[0]);
        Assert.Equal(packets[^1], frames[^1]);
    }
}
