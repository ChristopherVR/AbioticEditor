using SkiaSharp;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class ColorizedPixelInspection
{
    private readonly ITestOutputHelper _output;
    public ColorizedPixelInspection(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void SamplePixels_ColorizedArmor()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticEditor", "assets", "textures",
            "AbioticFactor", "Content", "Textures", "GUI", "ItemIcons",
            "itemicon_armor_chest_groupe.colored.png");
        if (!File.Exists(path))
        {
            _output.WriteLine("missing: " + path);
            return;
        }

        using var bmp = SKBitmap.Decode(path);
        _output.WriteLine($"{bmp.Width}x{bmp.Height}  ct={bmp.ColorType}  at={bmp.AlphaType}");

        long rs = 0, gs = 0, bs = 0, asum = 0, n = 0;
        int aMin = 255, aMax = 0;
        for (var y = 0; y < bmp.Height; y += 4)
        for (var x = 0; x < bmp.Width; x += 4)
        {
            var c = bmp.GetPixel(x, y);
            if (c.Alpha > 8) {
                rs += c.Red; gs += c.Green; bs += c.Blue; asum += c.Alpha; n++;
                if (c.Alpha < aMin) aMin = c.Alpha;
                if (c.Alpha > aMax) aMax = c.Alpha;
            }
        }
        if (n > 0) {
            _output.WriteLine($"opaque avg RGB: ({rs / n}, {gs / n}, {bs / n})  avgA={asum / n}  Arange=[{aMin}..{aMax}]  over {n} samples");
        }

        // Also force a re-decode with explicit Unpremul to get raw colors
        using var image = SKImage.FromEncodedData(path);
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var raw = new byte[bmp.Width * bmp.Height * 4];
        unsafe { fixed (byte* p = raw) { image.ReadPixels(info, (IntPtr)p, bmp.Width * 4, 0, 0); } }

        long rs2 = 0, gs2 = 0, bs2 = 0, asum2 = 0, n2 = 0;
        for (var i = 0; i < raw.Length; i += 16)
        {
            var a = raw[i + 3];
            if (a > 8) { rs2 += raw[i]; gs2 += raw[i + 1]; bs2 += raw[i + 2]; asum2 += a; n2++; }
        }
        if (n2 > 0) _output.WriteLine($"unpremul avg RGB: ({rs2 / n2}, {gs2 / n2}, {bs2 / n2})  avgA={asum2 / n2}  over {n2} samples");

        // Sample corners + center of SOURCE
        var srcPath = path.Replace(".colored.png", ".png");
        using var src = SKBitmap.Decode(srcPath);
        _output.WriteLine($"src: {src.ColorType} {src.AlphaType}");
        var samples = new[] { (0, 0), (10, 10), (bmp.Width / 2, bmp.Height / 2), (bmp.Width - 5, bmp.Height - 5) };
        foreach (var (sx, sy) in samples)
        {
            var sc = src.GetPixel(sx, sy);
            _output.WriteLine($"src({sx},{sy}): R={sc.Red} G={sc.Green} B={sc.Blue} A={sc.Alpha}");
        }
        _output.WriteLine("");
        foreach (var (sx, sy) in samples)
        {
            var dc = bmp.GetPixel(sx, sy);
            _output.WriteLine($"colorized({sx},{sy}): R={dc.Red} G={dc.Green} B={dc.Blue} A={dc.Alpha}");
        }
    }
}
