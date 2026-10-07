using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class LocalAddressTests
{
    [Fact]
    public void Loopback_is_this_pc()
    {
        Assert.True(LocalAddress.IsThisPc(IPAddress.Loopback, []));
        Assert.True(LocalAddress.IsThisPc(IPAddress.Parse("127.0.0.5"), []));
    }

    [Fact]
    public void One_of_our_own_lan_addresses_is_this_pc()
    {
        var own = new[] { IPAddress.Parse("192.168.1.10") };

        Assert.True(LocalAddress.IsThisPc(IPAddress.Parse("192.168.1.10"), own));
        Assert.False(LocalAddress.IsThisPc(IPAddress.Parse("192.168.1.11"), own));
    }

    [Fact]
    public void Every_address_this_pc_reports_is_this_pc()
    {
        Assert.True(LocalAddress.IsThisPc(IPAddress.Loopback));
        foreach (var address in LocalAddress.OwnAddresses())
            Assert.True(LocalAddress.IsThisPc(address));
    }
}
