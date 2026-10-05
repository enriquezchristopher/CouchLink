using CouchLink.Core.Diagnostics;

namespace CouchLink.Core.Tests;

public class RedactorTests
{
    private static readonly Redactor R = new([("PC-07", "<host>"), ("Topher", "<user>")]);

    [Fact]
    public void Machine_name_is_replaced_case_insensitively()
    {
        Assert.Equal("connect to <host> failed", R.Redact("connect to pc-07 failed"));
    }

    [Fact]
    public void User_name_in_paths_is_replaced()
    {
        Assert.Equal(@"at C:\Users\<user>\AppData\x.cs:line 4", R.Redact(@"at C:\Users\Topher\AppData\x.cs:line 4"));
    }

    [Theory]
    [InlineData("from 192.168.1.23:47803", "from <ip>:47803")]
    [InlineData("IP 10.0.0.5.", "IP <ip>.")]
    [InlineData("hosts 10.0.0.1 and 10.0.0.2", "hosts <ip> and <ip>")]
    [InlineData("fe80::1c2b:3d4e%12 down", "<ip>%12 down")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334", "<ip>")]
    public void Ip_addresses_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, R.Redact(input));
    }

    [Theory]
    [InlineData("version 1.2.3")]
    [InlineData("value 999.1.1.1")]
    [InlineData("at 19:40:12.123 started")]
    [InlineData("std::string")]
    public void Things_that_only_look_like_addresses_are_kept(string input)
    {
        Assert.Equal(input, R.Redact(input));
    }

    [Fact]
    public void Empty_or_one_letter_names_are_ignored()
    {
        var r = new Redactor([("", "<x>"), ("a", "<y>")]);
        Assert.Equal("a cat", r.Redact("a cat"));
    }

    [Fact]
    public void ForThisMachine_hides_this_PCs_name_and_user()
    {
        var text = $"{Environment.MachineName} {Environment.UserName}";
        var redacted = Redactor.ForThisMachine().Redact(text);
        Assert.DoesNotContain(Environment.MachineName, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, redacted, StringComparison.OrdinalIgnoreCase);
    }
}
