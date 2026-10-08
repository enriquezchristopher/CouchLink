using System.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class HostListTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.3");
    private static readonly IPAddress OtherLan = IPAddress.Parse("10.0.0.3");

    private readonly FakeTimeProvider _time = new();
    private readonly HostList _list;

    public HostListTests() => _list = new HostList(_time);

    [Fact]
    public void A_host_is_listed_with_its_address_and_counts()
    {
        _list.Seen(HostAnnounce.For(4, 9, "PC-03"), Lan);
        Assert.Equal(new FoundHost("PC-03", Lan, 4, 9, true), Assert.Single(_list.Current()));
    }

    [Fact]
    public void A_host_is_dropped_after_3s_without_an_announce()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _time.Advance(HostList.ExpireAfter - TimeSpan.FromMilliseconds(1));
        Assert.Single(_list.Current());
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(_list.Current());
    }

    [Fact]
    public void A_new_announce_keeps_the_host_and_updates_its_counts()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _time.Advance(TimeSpan.FromSeconds(2));
        _list.Seen(HostAnnounce.For(5, 9, "PC-03"), Lan);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(5, Assert.Single(_list.Current()).Players);
    }

    [Fact]
    public void A_host_seen_on_two_addresses_is_listed_once()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _list.Seen(HostAnnounce.For(1, 9, "pc-03"), OtherLan);
        Assert.Equal(OtherLan, Assert.Single(_list.Current()).Address);
    }

    [Fact]
    public void Loopback_never_replaces_a_lan_address()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _list.Seen(HostAnnounce.For(2, 9, "PC-03"), IPAddress.Loopback);
        var host = Assert.Single(_list.Current());
        Assert.Equal(Lan, host.Address);
        Assert.Equal(2, host.Players);

        var list = new HostList(_time);
        list.Seen(HostAnnounce.For(1, 9, "PC-03"), IPAddress.Loopback);
        list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        Assert.Equal(Lan, Assert.Single(list.Current()).Address);
    }

    [Fact]
    public void Hosts_are_sorted_by_name_and_other_versions_are_marked()
    {
        _list.Seen(HostAnnounce.For(0, 9, "PC-07"), Lan);
        _list.Seen(new HostAnnounce(2, 0, 9, "PC-01"), OtherLan);
        var hosts = _list.Current();
        Assert.Equal(["PC-01", "PC-07"], hosts.Select(h => h.Name));
        Assert.False(hosts[0].Compatible);
        Assert.True(hosts[1].Compatible);
    }
}
