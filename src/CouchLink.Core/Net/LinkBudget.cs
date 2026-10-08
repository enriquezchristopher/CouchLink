using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>One IPv4 address of a network card, with the card's link speed in bits per second (0 or less when unknown).</summary>
public readonly record struct NicAddress(string Nic, IPAddress Address, int PrefixLength, long BitsPerSecond);

/// <summary>
/// Whether the video stream fits the host's network links. Every client gets its own copy plus up to
/// 20% FEC parity, so a card carries bitrate x its clients x 1.2. More than 70% of the link warns;
/// the host decides what to do. Clients on no known card, and cards of unknown speed, are left out.
/// </summary>
public static class LinkBudget
{
    public const int MaxSharePercent = 70;

    public static string? Check(string quality, long bitRate, IEnumerable<IPAddress> clients, IReadOnlyList<NicAddress> nics)
    {
        string? warning = null;
        double worst = 0;
        var cards = clients.Select(c => CardFor(c, nics)).Where(n => n is not null).Select(n => n!.Value);
        foreach (var group in cards.GroupBy(n => n.Nic))
        {
            long link = group.First().BitsPerSecond;
            int count = group.Count();
            long needed = bitRate * count * (100 + VideoShardPacket.MaxParityPercent) / 100;
            if (needed * 100 <= link * MaxSharePercent)
                continue;
            double share = (double)needed / link;
            if (share <= worst)
                continue;
            worst = share;
            string who = count == 1 ? "1 client" : $"{count} clients";
            warning = $"{quality} to {who} needs about {Mbps(needed)} Mbps; this PC's network link is {Mbps(link)} Mbps. " +
                "Lower Quality or Resolution.";
        }
        return warning;
    }

    /// <summary>The card whose IPv4 subnet holds the client, skipping cards of unknown speed.</summary>
    public static NicAddress? CardFor(IPAddress client, IReadOnlyList<NicAddress> nics)
    {
        if (client.IsIPv4MappedToIPv6)
            client = client.MapToIPv4();
        if (client.AddressFamily != AddressFamily.InterNetwork)
            return null;
        foreach (var nic in nics)
        {
            if (nic.BitsPerSecond > 0 && nic.Address.AddressFamily == AddressFamily.InterNetwork
                && SameSubnet(client, nic.Address, nic.PrefixLength))
                return nic;
        }
        return null;
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, int prefixLength)
    {
        uint mask = prefixLength <= 0 ? 0 : prefixLength >= 32 ? uint.MaxValue : uint.MaxValue << (32 - prefixLength);
        return (ToUInt32(a) & mask) == (ToUInt32(b) & mask);
    }

    private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static long Mbps(long bitsPerSecond) => (long)Math.Round(bitsPerSecond / 1e6);
}
