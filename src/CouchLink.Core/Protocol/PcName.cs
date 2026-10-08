using System.Text;

namespace CouchLink.Core.Protocol;

/// <summary>A PC's name on the wire: UTF-8, 1-63 bytes, never cut in the middle of a character.</summary>
public static class PcName
{
    public const int MaxBytes = 63;
    private const string Placeholder = "Unknown PC";
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string ThisPc => Clip(Environment.MachineName);

    public static string Clip(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return Placeholder;

        var result = new StringBuilder();
        int bytes = 0;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxBytes)
                break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }

    public static byte[] Encode(string name) => Strict.GetBytes(Clip(name));

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string name)
    {
        name = "";
        if (bytes.Length is 0 or > MaxBytes)
            return false;
        try
        {
            name = Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
