using System.Net.NetworkInformation;
using System.Net.Sockets;
using CouchLink.Core.Net;

namespace CouchLink.App;

/// <summary>This PC's IPv4 addresses with their card's link speed, for <see cref="LinkBudget"/>. Read once when hosting starts.</summary>
internal static class HostLinks
{
    public static IReadOnlyList<NicAddress> Read()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => new NicAddress(n.Id, u.Address, u.PrefixLength, n.Speed)))
                .ToList();
        }
        catch (NetworkInformationException e)
        {
            AppServices.Log.Write($"Network cards unreadable, no link warning: {e.Message}");
            return [];
        }
    }
}
