using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace CouchLink.App.Presentation;

/// <summary>
/// The Join by address box. Only a full dotted-decimal IPv4 address. .NET would read "192.168" as
/// 192.0.0.168, "010.0.0.1" as octal and "0x7f.0.0.1" as hex, any of which would send the player
/// to a PC they never meant, so every octet is read as plain decimal here.
/// </summary>
internal static class AddressInput
{
    public const string Error = "Enter an IP address like 192.168.1.23.";

    public static bool TryParse(string? text, [NotNullWhen(true)] out IPAddress? address, [NotNullWhen(false)] out string? error)
    {
        string[] parts = (text ?? "").Trim().Split('.');
        if (parts.Length == 4)
        {
            var octets = new byte[4];
            bool ok = true;
            for (int i = 0; i < 4 && ok; i++)
                ok = TryOctet(parts[i], out octets[i]);
            if (ok)
            {
                address = new IPAddress(octets);
                error = null;
                return true;
            }
        }
        address = null;
        error = Error;
        return false;
    }

    private static bool TryOctet(string part, out byte value)
    {
        value = 0;
        if (part.Length is < 1 or > 3) return false;
        int n = 0;
        foreach (char c in part)
        {
            if (c is < '0' or > '9') return false;
            n = n * 10 + (c - '0');
        }
        if (n > 255) return false;
        value = (byte)n;
        return true;
    }
}
