using System.Net;
using System.Net.NetworkInformation;

namespace CouchLink.Core.Net;

/// <summary>Whether an address is this PC: loopback, or one of its own network addresses.</summary>
public static class LocalAddress
{
    public static bool IsThisPc(IPAddress address) => IsThisPc(address, OwnAddresses());

    public static bool IsThisPc(IPAddress address, IEnumerable<IPAddress> own) =>
        IPAddress.IsLoopback(address) || own.Contains(address);

    public static IEnumerable<IPAddress> OwnAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address);
}
