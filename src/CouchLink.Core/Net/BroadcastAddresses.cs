using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>
/// Where the host's announce goes: a directed broadcast (e.g. 192.168.1.255) on every IPv4 interface
/// that is up, because Windows sends 255.255.255.255 out of one adapter only. Loopback comes first so
/// a client on the host's own PC finds it even with no network.
/// </summary>
public static class BroadcastAddresses
{
    public static IPAddress For(IPAddress address, IPAddress mask)
    {
        var bytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] |= (byte)~maskBytes[i];
        return new IPAddress(bytes);
    }

    public static IReadOnlyList<IPAddress> Current()
    {
        var result = new List<IPAddress> { IPAddress.Loopback };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && unicast.IPv4Mask is { } mask
                    && !mask.Equals(IPAddress.Any))
                    result.Add(For(unicast.Address, mask));
            }
        }
        return result.Distinct().ToList();
    }
}
