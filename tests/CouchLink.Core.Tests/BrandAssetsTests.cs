using System.Buffers.Binary;

namespace CouchLink.Core.Tests;

/// <summary>The app icon and the GitHub social preview in assets/, built by eng/icon.</summary>
public class BrandAssetsTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "assets");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Folder, name));

    [Fact]
    public void The_icon_has_every_size_Windows_asks_for()
    {
        var ico = Read("couchlink.ico");

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0))); // reserved
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2))); // 1 = icon
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));

        var sizes = Enumerable.Range(0, count).Select(i => Entry(ico, i).Size).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, sizes);
    }

    [Fact]
    public void Only_the_largest_icon_is_a_PNG_and_the_rest_are_bitmaps()
    {
        // Every tool that reads icons handles bitmaps; PNG inside an icon is the classic exception for 256 px.
        var ico = Read("couchlink.ico");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));

        foreach (var e in Enumerable.Range(0, count).Select(i => Entry(ico, i)))
        {
            Assert.True(e.Offset + e.Length <= ico.Length, $"{e.Size} px image runs past the end of the file");
            var data = ico.AsSpan(e.Offset, e.Length);
            if (e.Size == 256)
                Assert.True(data[..8].SequenceEqual(PngSignature), "the 256 px image is not a PNG");
            else
                Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(data)); // BITMAPINFOHEADER
        }
    }

    [Fact]
    public void The_social_preview_is_the_size_GitHub_recommends_and_under_its_limit()
    {
        var png = Read("social-preview.png");

        Assert.True(png.AsSpan(0, 8).SequenceEqual(PngSignature), "not a PNG");
        Assert.Equal(1280, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(640, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.True(png.Length < 1_000_000, $"{png.Length} bytes; GitHub accepts under 1 MB");
    }

    private static (int Size, int Length, int Offset) Entry(byte[] ico, int index)
    {
        var e = ico.AsSpan(6 + index * 16, 16);
        int size = e[0] == 0 ? 256 : e[0];
        Assert.Equal(size, e[1] == 0 ? 256 : e[1]); // square
        return (size, BinaryPrimitives.ReadInt32LittleEndian(e[8..]), BinaryPrimitives.ReadInt32LittleEndian(e[12..]));
    }
}
