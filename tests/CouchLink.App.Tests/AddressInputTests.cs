using System.Net;
using CouchLink.App.Presentation;

namespace CouchLink.App.Tests;

public class AddressInputTests
{
    [Theory]
    [InlineData("192.168.1.23", "192.168.1.23")]
    [InlineData("  10.0.0.5 ", "10.0.0.5")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    public void Four_part_IPv4_addresses_are_accepted(string text, string expected)
    {
        Assert.True(AddressInput.TryParse(text, out var address, out var error));
        Assert.Equal(IPAddress.Parse(expected), address);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("pc-07")]
    [InlineData("192.168")]
    [InlineData("10.1")]
    [InlineData("1")]
    [InlineData("192.168.1.256")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void Anything_else_is_rejected_with_the_usual_message(string? text)
    {
        Assert.False(AddressInput.TryParse(text, out var address, out var error));
        Assert.Null(address);
        Assert.Equal("Enter an IP address like 192.168.1.23.", error);
    }
}
