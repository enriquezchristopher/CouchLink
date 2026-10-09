using SkiaSharp;
using Svg.Skia;

// Usage: dotnet run --project eng/icon [repo-root]
// Reads assets/couchlink.svg, assets/couchlink-small.svg and assets/social-preview.svg and writes
// assets/couchlink.ico and assets/social-preview.png.
string root = args.Length > 0 ? args[0] : FindRoot();
string assets = Path.Combine(root, "assets");

var full = Load(Path.Combine(assets, "couchlink.svg"));
var small = Load(Path.Combine(assets, "couchlink-small.svg"));

// 16 and 24 px use the simplified drawing; everything larger has the chain link.
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var images = sizes.Select(size => (Size: size, Data: size == 256
    ? EncodePng(Render(full, size, size))
    : EncodeBitmap(Render(size <= 24 ? small : full, size, size)))).ToList();
File.WriteAllBytes(Path.Combine(assets, "couchlink.ico"), BuildIcon(images));

var social = Load(Path.Combine(assets, "social-preview.svg"));
File.WriteAllBytes(Path.Combine(assets, "social-preview.png"), EncodePng(Render(social, 1280, 640)));

Console.WriteLine($"Wrote {Path.Combine(assets, "couchlink.ico")} ({sizes.Length} sizes) and social-preview.png.");
return 0;

static string FindRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CouchLink.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run this from inside the CouchLink repo, or pass its folder.");
}

static SKPicture Load(string path)
{
    var svg = new SKSvg();
    svg.Load(path);
    return svg.Picture ?? throw new InvalidOperationException($"{path} did not load.");
}

static SKBitmap Render(SKPicture picture, int width, int height)
{
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    var bounds = picture.CullRect;
    canvas.Scale(width / bounds.Width, height / bounds.Height);
    canvas.DrawPicture(picture);
    return bitmap;
}

static byte[] EncodePng(SKBitmap bitmap)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// An icon image as a 32-bit bitmap: BITMAPINFOHEADER, then the pixels bottom row first as straight (not
// premultiplied) BGRA, then a 1-bit AND mask that is all zero because the alpha channel does the work.
static byte[] EncodeBitmap(SKBitmap bitmap)
{
    int w = bitmap.Width, h = bitmap.Height;
    int maskRow = (w + 31) / 32 * 4;
    var pixels = bitmap.Bytes;
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(40);
    writer.Write(w);
    writer.Write(h * 2);
    writer.Write((short)1);
    writer.Write((short)32);
    writer.Write(0);
    writer.Write(w * h * 4 + maskRow * h);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    for (int y = h - 1; y >= 0; y--)
    {
        for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                b = Unpremultiply(b, a);
                g = Unpremultiply(g, a);
                r = Unpremultiply(r, a);
            }
            writer.Write(b);
            writer.Write(g);
            writer.Write(r);
            writer.Write(a);
        }
    }
    writer.Write(new byte[maskRow * h]);
    return stream.ToArray();
}

static byte Unpremultiply(byte color, byte alpha) => (byte)Math.Min(255, (color * 255 + alpha / 2) / alpha);

static byte[] BuildIcon(List<(int Size, byte[] Data)> images)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, data) in images)
    {
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0); // no palette
        writer.Write((byte)0);
        writer.Write((short)1); // planes
        writer.Write((short)32); // bits per pixel
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images)
        writer.Write(data);
    return stream.ToArray();
}
