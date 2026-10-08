using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class HostAnnounceTests
{
    [Fact]
    public void Round_trips()
    {
        var announce = HostAnnounce.For(players: 4, capacity: 9, name: "PC-03");

        Assert.True(HostAnnounce.TryParse(announce.ToArray(), out var parsed));

        Assert.Equal(announce, parsed);
        Assert.Equal(Wire.Version, parsed.Version);
        Assert.True(parsed.Compatible);
    }

    [Fact]
    public void An_announce_from_another_version_still_parses_and_is_not_compatible()
    {
        var bytes = new HostAnnounce(Version: 2, Players: 1, Capacity: 9, Name: "PC-03").ToArray();

        Assert.True(HostAnnounce.TryParse(bytes, out var parsed));

        Assert.Equal(2, parsed.Version);
        Assert.False(parsed.Compatible);
    }

    [Fact]
    public void Truncated_wrong_type_or_inconsistent_announces_are_rejected()
    {
        var good = HostAnnounce.For(1, 9, "PC-03").ToArray();

        Assert.False(HostAnnounce.TryParse(good.AsSpan(0, good.Length - 1), out _)); // truncated name
        Assert.False(HostAnnounce.TryParse(good.AsSpan(0, 6), out _));

        var wrongType = (byte[])good.Clone();
        wrongType[3] = Wire.TypeInput;
        Assert.False(HostAnnounce.TryParse(wrongType, out _));

        var tooMany = (byte[])good.Clone();
        tooMany[4] = 10; // players > capacity
        Assert.False(HostAnnounce.TryParse(tooMany, out _));

        var noName = (byte[])good.Clone();
        noName[6] = 0;
        Assert.False(HostAnnounce.TryParse(noName.AsSpan(0, 7), out _));
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(7);
        for (int i = 0; i < 10_000; i++)
        {
            var bytes = new byte[random.Next(0, 80)];
            random.NextBytes(bytes);
            if (bytes.Length >= 4)
            {
                bytes[0] = 0x43;
                bytes[1] = 0x4C;
                bytes[3] = Wire.TypeHostAnnounce;
            }
            HostAnnounce.TryParse(bytes, out _);
        }
    }
}
