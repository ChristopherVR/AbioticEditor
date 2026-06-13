using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using SkiaSharp;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Calibration tool: composites the door cloud of a sub-level over its in-game sector
/// map texture in 8 orientation variants (4 rotations x optional X flip), saved to
/// tools/shots/calib/. A human (or the assistant via screenshots) picks the variant
/// where doors land on the drawn corridors; the result is baked into
/// SectorMapCalibration in Core.
/// </summary>
public class SectorMapCalibrationProbe
{
    private readonly ITestOutputHelper _output;

    public SectorMapCalibrationProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Generate_Composites_ForAllSectorMaps()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var outDir = Path.Combine(FindRepoRoot(), "tools", "shots", "calib");
        Directory.CreateDirectory(outDir);

        var maps = SectorMapCatalog.LoadFrom(provider);
        _output.WriteLine($"sector maps: {maps.Count}");

        foreach (var map in maps)
        {
            var texturePath = provider.ExtractTextureByGameRef(map.TexturePath);
            if (texturePath is null) { _output.WriteLine($"{map.Row}: texture extract failed"); continue; }

            var actors = DoorLocationResolver.ForMap(provider, map.LevelFileName);
            if (actors.Count == 0) { _output.WriteLine($"{map.Row}: no actor positions for {map.LevelFileName}"); continue; }

            // Door cloud = the pins; FULL actor cloud = the calibration reference
            // (covers the playable area far more evenly than the doors alone).
            var doorPoints = actors
                .Where(kv => kv.Key.Contains("Door", StringComparison.OrdinalIgnoreCase)
                          || kv.Key.Contains("Hatch", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .ToList();
            var cloud = actors.Values.ToList();
            if (doorPoints.Count < 4) { _output.WriteLine($"{map.Row}: only {doorPoints.Count} doors; skipping"); continue; }

            using var texture = SKBitmap.Decode(texturePath);
            if (texture is null) { _output.WriteLine($"{map.Row}: decode failed"); continue; }

            var content = SectorMapCalibration.DetectContentRect(texture);
            for (var variant = 0; variant < 8; variant++)
            {
                var path = Path.Combine(outDir, $"{map.LevelFileName}-v{variant}.png");
                RenderComposite(texture, content, cloud, doorPoints, variant, path);
            }
            _output.WriteLine($"{map.Row} ({map.LevelFileName}): {doorPoints.Count} doors, cloud {cloud.Count}, content {content} -> 8 variants");
        }
    }

    private static void RenderComposite(
        SKBitmap texture,
        SKRectI content,
        IReadOnlyList<DoorWorldLocation> cloud,
        IReadOnlyList<DoorWorldLocation> doors,
        int variant,
        string outPath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(texture.Width, texture.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(texture, 0, 0);

        using var contentOutline = new SKPaint
        {
            Color = new SKColor(0, 90, 255, 160),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        canvas.DrawRect(content, contentOutline);

        var project = SectorMapCalibration.BuildProjector(cloud, content, variant);
        using var dot = new SKPaint { Color = new SKColor(255, 40, 40, 230), IsAntialias = true };
        using var ring = new SKPaint
        {
            Color = new SKColor(255, 255, 0, 230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        foreach (var d in doors)
        {
            var (x, y) = project(d);
            canvas.DrawCircle(x, y, 4, dot);
            canvas.DrawCircle(x, y, 6, ring);
        }

        using var label = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 28);
        canvas.DrawText($"v{variant}", 12, 36, font, label);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(outPath);
        data.SaveTo(fs);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AbioticEditor.slnx")))
        {
            dir = dir.Parent!;
        }
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
