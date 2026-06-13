using SkiaSharp;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class SkiaRoundtripTests
{
    private readonly ITestOutputHelper _output;
    public SkiaRoundtripTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void WriteKnownColorsRoundtrip()
    {
        const int w = 16;
        const int h = 16;
        var bytes = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            // Solid red, full opacity
            bytes[i * 4 + 0] = 200; // R
            bytes[i * 4 + 1] = 50;  // G
            bytes[i * 4 + 2] = 80;  // B
            bytes[i * 4 + 3] = 255; // A
        }

        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var tempPath = Path.Combine(Path.GetTempPath(), $"skia-rt-{Guid.NewGuid():N}.png");
        try
        {
            unsafe
            {
                fixed (byte* p = bytes)
                {
                    using var pixmap = new SKPixmap(info, (IntPtr)p, w * 4);
                    using var image = SKImage.FromPixelCopy(pixmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = File.Create(tempPath);
                    data.SaveTo(stream);
                }
            }

            using var loaded = SKBitmap.Decode(tempPath);
            _output.WriteLine($"loaded: {loaded.ColorType} {loaded.AlphaType}");
            var c = loaded.GetPixel(w / 2, h / 2);
            _output.WriteLine($"center: R={c.Red} G={c.Green} B={c.Blue} A={c.Alpha}");

            Assert.Equal(200, c.Red);
            Assert.Equal(50, c.Green);
            Assert.Equal(80, c.Blue);
            Assert.Equal(255, c.Alpha);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
