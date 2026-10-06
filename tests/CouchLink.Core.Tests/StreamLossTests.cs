using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

/// <summary>
/// Spec section 9: FEC repair under simulated 1-20% loss. Runs 10 s of 60 fps video through
/// packetizer -> random packet loss -> assembler -> gate, with the host answering keyframe
/// requests on the next frame. Seeds are fixed, so results are repeatable.
/// </summary>
public class StreamLossTests
{
    private const int Frames = 600;
    private static readonly TimeSpan FrameTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private sealed record Result(int Decoded, int Corrupt, VideoReceiveStats Stats);

    private static Result Run(double loss, int seed)
    {
        var rng = new Random(seed);
        var packetizer = new FramePacketizer(streamId: 1);
        var gate = new DecodeGate();
        var assembler = new FrameAssembler(gate.FrameLost);
        var sent = new Dictionary<uint, byte[]>();
        bool keyframeWanted = true;
        int decoded = 0, corrupt = 0;

        for (uint n = 0; n < Frames; n++)
        {
            var now = FrameTime * n;
            bool keyframe = keyframeWanted;
            keyframeWanted = false;
            var frame = new byte[keyframe ? 150_000 : 15_000 + rng.Next(15_000)];
            rng.NextBytes(frame);
            sent[n] = frame;

            foreach (var packet in packetizer.Packetize(n, frame, keyframe))
            {
                if (rng.NextDouble() < loss)
                    continue;
                if (assembler.Add(packet, now) is { } done && gate.Accept(done))
                {
                    decoded++;
                    if (!done.Data.AsSpan().SequenceEqual(sent[done.Number]))
                        corrupt++;
                }
            }
            assembler.AbandonStale(now + FrameTime);
            if (gate.ShouldRequestKeyframe(now + FrameTime))
                keyframeWanted = true;
        }
        return new Result(decoded, corrupt, assembler.Stats);
    }

    [Fact]
    public void No_loss_decodes_every_frame()
    {
        var result = Run(0, seed: 1);
        Assert.Equal(Frames, result.Decoded);
        Assert.Equal(0, result.Stats.FramesLost);
    }

    [Theory]
    [InlineData(0.01, 0.95)]
    [InlineData(0.05, 0.85)]
    [InlineData(0.10, 0.40)]
    public void Most_frames_survive_random_loss(double loss, double minDecoded)
    {
        var result = Run(loss, seed: 1234);
        Assert.Equal(0, result.Corrupt);
        Assert.True(result.Decoded >= minDecoded * Frames, $"decoded {result.Decoded} of {Frames} at {loss:P0} loss");
        Assert.True(result.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Twenty_percent_loss_never_shows_a_corrupt_frame()
    {
        var result = Run(0.20, seed: 1234);
        Assert.Equal(0, result.Corrupt);
        Assert.True(result.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Measured_loss_matches_the_simulated_loss()
    {
        Assert.InRange(Run(0.05, seed: 99).Stats.LossPercent, 3.5, 6.5);
    }
}
