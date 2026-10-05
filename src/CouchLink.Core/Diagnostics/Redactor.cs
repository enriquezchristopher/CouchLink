using System.Text.RegularExpressions;

namespace CouchLink.Core.Diagnostics;

/// <summary>
/// Removes personal data from text that will be posted publicly: given names
/// (PC name, user name) and IPv4/IPv6 addresses.
/// </summary>
public sealed class Redactor
{
    private const string IpPlaceholder = "<ip>";

    private static readonly Regex Ipv4 = new(
        @"(?<!\d)(?<!\d\.)(?<!version[= ])(?<!v)(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\.?\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Candidate IPv6; only replaced when it has "::" or all 7 colons, so times like 19:40:12 survive.
    private static readonly Regex Ipv6Candidate = new(
        @"(?<![\w:])[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}(?![\w:])",
        RegexOptions.Compiled);

    private readonly (Regex Pattern, string Placeholder)[] _names;

    public Redactor(IEnumerable<(string Value, string Placeholder)> names)
    {
        _names = names
            .Where(n => n.Value.Length >= 2)
            .Select(n => (new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(n.Value) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase), n.Placeholder))
            .ToArray();
    }

    public static Redactor ForThisMachine() =>
        new([
            (Environment.MachineName, "<host>"),
            (Environment.UserName, "<user>"),
            (Path.GetFileName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/')), "<user>")]);

    public string Redact(string text)
    {
        foreach (var (pattern, placeholder) in _names)
            text = pattern.Replace(text, placeholder);
        text = Ipv4.Replace(text, IpPlaceholder);
        text = Ipv6Candidate.Replace(text, m =>
            m.Value.Contains("::") || m.Value.Count(c => c == ':') == 7 ? IpPlaceholder : m.Value);
        return text;
    }
}
