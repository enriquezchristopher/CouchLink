using System.Net;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamTargetsTests
{
    private const int Port = 47802;
    private static readonly IPAddress A = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress B = IPAddress.Parse("192.168.1.21");

    [Fact]
    public void An_added_client_gets_its_address_on_the_port()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        Assert.Equal(new IPEndPoint(A, Port), Assert.Single(targets.Current()));
    }

    [Fact]
    public void Adding_a_slot_again_with_a_new_address_replaces_it()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        targets.Add(2, B);
        Assert.Equal(new IPEndPoint(B, Port), Assert.Single(targets.Current()));
    }

    [Fact]
    public void A_removed_slot_gets_nothing()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        Assert.True(targets.Remove(2));
        Assert.False(targets.Remove(2));
        Assert.Empty(targets.Current());
    }

    [Fact]
    public void Two_slots_on_one_PC_get_one_copy_until_both_are_removed()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        targets.Add(3, A);
        Assert.Single(targets.Current());
        targets.Remove(2);
        Assert.Single(targets.Current());
        targets.Remove(3);
        Assert.Empty(targets.Current());
    }
}
