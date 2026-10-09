using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace CouchLink.App.Presentation;

/// <summary>
/// The Join by address box. Only a full IPv4 address: .NET reads "192.168" as 192.0.0.168, which
/// would send the player to a PC they never meant.
/// </summary>
internal static class AddressInput
{
    public const string Error = "Enter an IP address like 192.168.1.23.";

    public static bool TryParse(string? text, [NotNullWhen(true)] out IPAddress? address, [NotNullWhen(false)] out string? error)
    {
        string trimmed = (text ?? "").Trim();
        if (trimmed.Split('.').Length == 4
            && IPAddress.TryParse(trimmed, out var parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed;
            error = null;
            return true;
        }
        address = null;
        error = Error;
        return false;
    }
}
