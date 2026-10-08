using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class BroadcastAddressesTests
{
    [Theory]
    [InlineData("192.168.1.23", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.1.2.3", "255.255.0.0", "10.1.255.255")]
    [InlineData("172.16.5.9", "255.255.255.255", "172.16.5.9")]
    public void Directed_broadcast_sets_the_host_bits(string address, string mask, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), BroadcastAddresses.For(IPAddress.Parse(address), IPAddress.Parse(mask)));
    }

    [Fact]
    public void Current_starts_with_loopback_and_holds_only_distinct_IPv4_addresses()
    {
        var current = BroadcastAddresses.Current();
        Assert.Equal(IPAddress.Loopback, current[0]);
        Assert.All(current, a => Assert.Equal(AddressFamily.InterNetwork, a.AddressFamily));
        Assert.Equal(current.Count, current.Distinct().Count());
    }
}
