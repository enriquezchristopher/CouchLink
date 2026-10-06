namespace CouchLink.Core.Fec;

/// <summary>
/// Arithmetic in GF(2^8) with the polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D), generator 2.
/// Addition and subtraction are XOR.
/// </summary>
public static class GaloisField
{
    private static readonly byte[] Exp = new byte[510];
    private static readonly byte[] Log = new byte[256];
    private static readonly byte[] Products = new byte[256 * 256];

    static GaloisField()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Exp[i + 255] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
                x ^= 0x11D;
        }

        for (int a = 1; a < 256; a++)
            for (int b = 1; b < 256; b++)
                Products[(a << 8) | b] = Exp[Log[a] + Log[b]];
    }

    public static byte Multiply(byte a, byte b) => Products[(a << 8) | b];

    public static byte Inverse(byte a)
    {
        if (a == 0)
            throw new DivideByZeroException("0 has no inverse in GF(256).");
        return Exp[255 - Log[a]];
    }

    /// <summary>destination[i] ^= factor * source[i] for every i.</summary>
    public static void MultiplyAdd(byte factor, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (factor == 0)
            return;
        var row = Products.AsSpan(factor << 8, 256);
        for (int i = 0; i < source.Length; i++)
            destination[i] ^= row[source[i]];
    }
}
