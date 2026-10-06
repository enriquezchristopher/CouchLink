using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>One second of F2 overlay numbers. Latency = host delay + half the round trip + client delay.</summary>
public readonly record struct StatsSample(
    double Fps, double Mbps, double LossPercent, long FecRepairs,
    TimeSpan? Latency, TimeSpan HostDelay, TimeSpan? Network, TimeSpan ClientDelay);

/// <summary>Turns the client's running totals into per-second numbers, once a second. Not thread-safe.</summary>
public sealed class StatsWindow
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(1);

    private (VideoClientStats Stats, long Shown, TimeSpan At)? _last;

    public StatsSample? Update(VideoClientStats stats, long framesShown, TimeSpan clientDelay, TimeSpan now)
    {
        if (_last is not { } last)
        {
            _last = (stats, framesShown, now);
            return null;
        }
        var elapsed = now - last.At;
        if (elapsed < Length)
            return null;
        _last = (stats, framesShown, now);

        double seconds = elapsed.TotalSeconds;
        var r = stats.Receive;
        var p = last.Stats.Receive;
        long expected = r.DataShardsExpected - p.DataShardsExpected;
        long missing = r.DataShardsMissing - p.DataShardsMissing;
        TimeSpan? network = stats.RoundTrip / 2;
        return new StatsSample(
            Fps: (framesShown - last.Shown) / seconds,
            Mbps: (r.PacketsReceived - p.PacketsReceived) * (double)VideoShardPacket.Size * 8 / seconds / 1e6,
            LossPercent: expected <= 0 ? 0 : 100.0 * missing / expected,
            FecRepairs: (long)Math.Round((r.ShardsRecovered - p.ShardsRecovered) / seconds),
            Latency: network is { } n ? stats.HostDelay + n + clientDelay : null,
            HostDelay: stats.HostDelay,
            Network: network,
            ClientDelay: clientDelay);
    }
}
