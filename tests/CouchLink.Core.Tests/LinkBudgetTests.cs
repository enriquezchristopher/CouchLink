using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class LinkBudgetTests
{
    private static readonly NicAddress Lan100 = new("lan", IPAddress.Parse("192.168.1.10"), 24, 100_000_000);

    private static IPAddress[] Clients(int count) =>
        Enumerable.Range(20, count).Select(i => IPAddress.Parse($"192.168.1.{i}")).ToArray();

    [Fact]
    public void Warns_when_the_stream_needs_more_than_70_percent_of_the_link()
    {
        // 25 Mbps x 6 clients x 1.2 FEC = 180 Mbps on a 100 Mbps card.
        Assert.Equal(
            "High to 6 clients needs about 180 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, Clients(6), [Lan100]));
    }

    [Fact]
    public void Exactly_70_percent_is_fine_and_one_bit_more_warns()
    {
        var lan120 = Lan100 with { BitsPerSecond = 120_000_000 }; // 70 Mbps x 1.2 = 84 Mbps = 70%
        Assert.Null(LinkBudget.Check("Max", 70_000_000, Clients(1), [lan120]));
        Assert.Equal(
            "Max to 1 client needs about 84 Mbps; this PC's network link is 120 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("Max", 70_000_001, Clients(1), [lan120]));
    }

    [Fact]
    public void Gigabit_has_room_for_max_at_1080p60_with_a_full_room()
    {
        var gigabit = Lan100 with { BitsPerSecond = 1_000_000_000 }; // 50 x 9 x 1.2 = 540 Mbps
        Assert.Null(LinkBudget.Check("Max", 50_000_000, Clients(9), [gigabit]));
    }

    [Fact]
    public void No_clients_no_warning()
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, [], [Lan100]));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_card_with_an_unknown_speed_is_left_out(long speed)
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, Clients(9), [Lan100 with { BitsPerSecond = speed }]));
    }

    [Fact]
    public void A_client_on_no_cards_subnet_is_left_out()
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, [IPAddress.Parse("10.9.9.9")], [Lan100]));
    }

    [Fact]
    public void An_IPv4_mapped_client_address_matches_its_card()
    {
        Assert.Equal(Lan100, LinkBudget.CardFor(IPAddress.Parse("::ffff:192.168.1.20"), [Lan100]));
    }

    [Fact]
    public void Clients_are_counted_on_the_card_of_their_subnet_not_the_first_or_fastest()
    {
        var wifi = new NicAddress("wifi", IPAddress.Parse("10.0.0.5"), 24, 1_000_000_000);
        Assert.Equal(Lan100, LinkBudget.CardFor(IPAddress.Parse("192.168.1.20"), [wifi, Lan100]));
        Assert.Equal(
            "High to 6 clients needs about 180 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, Clients(6), [wifi, Lan100]));
    }

    [Fact]
    public void With_two_loaded_cards_the_worst_one_is_reported()
    {
        var second = new NicAddress("lan2", IPAddress.Parse("192.168.2.10"), 24, 100_000_000);
        IPAddress[] clients = [.. Clients(3), IPAddress.Parse("192.168.2.20")];
        // lan: 25 x 3 x 1.2 = 90 Mbps (90%); lan2: 30 Mbps (30%, fine).
        Assert.Equal(
            "High to 3 clients needs about 90 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, clients, [Lan100, second]));
    }
}
