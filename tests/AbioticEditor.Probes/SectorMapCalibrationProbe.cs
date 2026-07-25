using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using SkiaSharp;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Calibration tool for <see cref="SectorMapCalibration"/>, which pins doors on the game's
/// drawn sector maps. Output lands in tools/shots/calib/ for a human (or the assistant via
/// screenshots) to check.
///
/// <c>Solve_Fits</c> searches orientation, scale and offset per level and prints the affine to
/// paste into Core. <c>Composite_Fits</c> renders the baked fits with door pins and labelled
/// landmark actors so the result can be verified by eye. <c>Dump_RawTexturesWithGrid</c>
/// overlays a tenths grid on the bare drawings.
/// </summary>
public class SectorMapCalibrationProbe
{
    private readonly ITestOutputHelper _output;

    public SectorMapCalibrationProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private const int MaskW = 256;
    private const int MaskH = 128;

    /// <summary>
    /// Where the floor plan actually sits on the page, as texture fractions, measured by eye
    /// off <see cref="Dump_RawTexturesWithGrid"/>. Several pamphlets devote half the page to a
    /// legend or a wall of prose, which is "not background" and therefore drags an unguided
    /// search onto the text. Levels absent here search the whole page.
    /// </summary>
    private static readonly Dictionary<string, (double L, double T, double R, double B)> DrawingArea =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Facility_Office1"] = (0.29, 0.02, 0.79, 0.99),
            ["Facility_Office2"] = (0.20, 0.02, 0.83, 0.99),
            ["Facility_Office3"] = (0.40, 0.02, 0.72, 1.00),
            ["Facility_Labs"] = (0.02, 0.02, 0.68, 0.98),
            ["Facility_MFWest"] = (0.03, 0.03, 0.98, 0.97),
            ["Facility_Pens"] = (0.03, 0.03, 0.85, 0.98),
            ["Facility_Dam"] = (0.02, 0.02, 0.50, 0.98),
            ["Facility_DarkFusion"] = (0.34, 0.02, 0.70, 0.98),
        };

    /// <summary>
    /// Actors whose name gives away a spot the drawings label, so a fit can be confirmed
    /// rather than merely scored.
    /// </summary>
    private static readonly (string Needle, string Label, SKColor Colour)[] Landmarks =
    {
        ("Toilet", "WC", new SKColor(0, 120, 255)),
        ("Urinal", "WC", new SKColor(0, 120, 255)),
        ("Sink", "WC", new SKColor(0, 160, 255)),
        ("Tram", "TRAM", new SKColor(255, 0, 200)),
        ("Vending", "VEND", new SKColor(255, 140, 0)),
        ("Elevator", "LIFT", new SKColor(180, 0, 255)),
    };

    /// <summary>
    /// Solves each level's fit instead of guessing it: rasterises the drawn floor plan into a
    /// mask, then hunts over orientation, scale and offset for the placement that puts the
    /// most of the level's actor cloud on the plan while still spreading across it. Prints
    /// the affine to bake into <see cref="SectorMapCalibration"/>.
    ///
    /// A bounding-box fit (what this replaced) fails whenever the level extends past what the
    /// pamphlet draws, which is most of them.
    /// </summary>
    [Fact]
    public void Solve_Fits()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var maps = SectorMapCatalog.LoadFrom(provider);
        foreach (var (level, fit) in SectorMapCalibration.CalibratedLevels)
        {
            using var texture = LoadTexture(provider, maps, fit);
            if (texture is null) { _output.WriteLine($"{level}: texture unavailable"); continue; }

            var cloud = DoorLocationResolver.ForMap(provider, level).Values.ToList();
            if (cloud.Count == 0) { _output.WriteLine($"{level}: no actors"); continue; }

            // Print every orientation's best placement, not just the winner: rotations 180
            // degrees apart score almost identically, so the runner-up is often the right one
            // and only the landmark composites can tell them apart.
            foreach (var candidate in SolveFit(texture, cloud, DrawingArea.GetValueOrDefault(level, (0, 0, 1, 1)))
                         .OrderByDescending(c => c.Score))
            {
                _output.WriteLine(
                    $"[\"{level}\"] = new(\"{fit.PamphletRow}\", {candidate.Variant}, "
                    + $"{N(candidate.ScaleX)}, {N(candidate.ScaleY)}, {N(candidate.OffsetX)}, {N(candidate.OffsetY)}), "
                    + $"// score {candidate.Score:F3}");
            }
        }
    }

    private static string N(double v)
        => v.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders the baked fits: every door as a pin, plus labelled landmark actors. If the WC
    /// dots sit on the drawing's restroom icon and the LIFT dots on its lifts, the fit is right.
    /// </summary>
    [Fact]
    public void Composite_Fits()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var outDir = OutDir("fits");
        var maps = SectorMapCatalog.LoadFrom(provider);

        foreach (var (level, fit) in SectorMapCalibration.CalibratedLevels)
        {
            using var texture = LoadTexture(provider, maps, fit);
            if (texture is null) { _output.WriteLine($"{level}: texture unavailable"); continue; }

            var actors = DoorLocationResolver.ForMap(provider, level);
            if (actors.Count == 0) { _output.WriteLine($"{level}: no actors"); continue; }

            using var surface = SKSurface.Create(new SKImageInfo(texture.Width, texture.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(texture, 0, 0);

            using var dot = new SKPaint { Color = new SKColor(255, 40, 40, 230), IsAntialias = true };
            using var ring = new SKPaint
            {
                Color = new SKColor(255, 255, 0, 230),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
            };
            using var small = new SKFont(SKTypeface.Default, 16);
            using var big = new SKFont(SKTypeface.Default, 30);
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true };

            var doors = 0;
            foreach (var (name, loc) in actors)
            {
                if (!name.Contains("Door", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Hatch", StringComparison.OrdinalIgnoreCase)) continue;
                var (fx, fy) = SectorMapCalibration.Project(fit, loc);
                var x = (float)(fx * texture.Width);
                var y = (float)(fy * texture.Height);
                canvas.DrawCircle(x, y, 4, dot);
                canvas.DrawCircle(x, y, 6, ring);
                doors++;
            }

            var landmarks = 0;
            foreach (var (name, loc) in actors)
            {
                var hit = Landmarks.FirstOrDefault(l => name.Contains(l.Needle, StringComparison.OrdinalIgnoreCase));
                if (hit.Label is null) continue;
                var (fx, fy) = SectorMapCalibration.Project(fit, loc);
                var x = (float)(fx * texture.Width);
                var y = (float)(fy * texture.Height);
                using var paint = new SKPaint { Color = hit.Colour, IsAntialias = true };
                canvas.DrawCircle(x, y, 5, paint);
                canvas.DrawText(hit.Label, x + 7, y + 5, small, paint);
                landmarks++;
            }

            canvas.DrawText($"{level} v{fit.Variant}", 12, 38, big, black);
            Save(surface, Path.Combine(outDir, $"{level}.png"));
            _output.WriteLine($"{level}: {doors} doors, {landmarks} landmarks");
        }
    }

    /// <summary>
    /// Sub-levels share one world space, so a fit solved for one of them is really a fit for
    /// that DRAWING - any sub-level whose actors physically sit inside the depicted area should
    /// pin onto it correctly. This checks that: for every cooked sub-level with no fit of its
    /// own, it reports which calibrated drawing (if any) actually contains its doors. If this
    /// holds, the six calibrated drawings can cover far more than six sub-levels.
    /// </summary>
    [Fact]
    public void Probe_WhichDrawingContainsEachUncalibratedLevel()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var maps = SectorMapCatalog.LoadFrom(provider);
        var masks = new Dictionary<string, (SKBitmap Texture, bool[] Mask)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (level, fit) in SectorMapCalibration.CalibratedLevels)
        {
            var texture = LoadTexture(provider, maps, fit);
            if (texture is null) continue;
            masks[level] = (texture, BuildPlanMask(texture, DrawingArea.GetValueOrDefault(level, (0, 0, 1, 1))));
        }

        var levels = provider.AssetPaths
            .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)
                     && p.StartsWith("AbioticFactor/Content/Maps/", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n) && !n!.Contains("_BuiltData", StringComparison.OrdinalIgnoreCase))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var level in levels)
        {
            var doors = DoorLocationResolver.ForMap(provider, level)
                .Where(kv => kv.Key.Contains("Door", StringComparison.OrdinalIgnoreCase)
                          || kv.Key.Contains("Hatch", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .ToList();
            if (doors.Count < 3) continue;

            var best = string.Empty;
            var bestScore = 0.0;
            foreach (var (owner, fit) in SectorMapCalibration.CalibratedLevels)
            {
                if (!masks.TryGetValue(owner, out var art)) continue;
                var onPlan = 0;
                foreach (var door in doors)
                {
                    var (fx, fy) = SectorMapCalibration.Project(fit, door);
                    if (fx is < 0 or > 1 || fy is < 0 or > 1) continue;
                    var mx = Math.Clamp((int)(fx * MaskW), 0, MaskW - 1);
                    var my = Math.Clamp((int)(fy * MaskH), 0, MaskH - 1);
                    if (art.Mask[my * MaskW + mx]) onPlan++;
                }
                var score = onPlan / (double)doors.Count;
                if (score > bestScore) { bestScore = score; best = owner; }
            }

            var own = SectorMapCalibration.FitFor(level) is null ? "" : " (has own fit)";
            _output.WriteLine($"  {level}\t{doors.Count} doors\tbest={(best.Length == 0 ? "-" : best)}\t{bestScore:P0}{own}");
        }

        foreach (var art in masks.Values) art.Texture.Dispose();
    }

    [Fact]
    public void Dump_RawTexturesWithGrid()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("no install"); return; }

        var outDir = OutDir("raw");
        foreach (var map in SectorMapCatalog.LoadFrom(provider))
        {
            var texturePath = provider.ExtractTextureByGameRef(map.TexturePath);
            if (texturePath is null) continue;
            using var texture = SKBitmap.Decode(texturePath);
            if (texture is null) continue;

            using var surface = SKSurface.Create(new SKImageInfo(texture.Width, texture.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(texture, 0, 0);
            using var line = new SKPaint { Color = new SKColor(255, 0, 255, 150), StrokeWidth = 1 };
            using var label = new SKPaint { Color = new SKColor(255, 0, 255, 220), IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 18);
            for (var i = 1; i < 10; i++)
            {
                var x = texture.Width * i / 10f;
                var y = texture.Height * i / 10f;
                canvas.DrawLine(x, 0, x, texture.Height, line);
                canvas.DrawLine(0, y, texture.Width, y, line);
                canvas.DrawText($".{i}", x + 3, 20, font, label);
                canvas.DrawText($".{i}", 3, y - 3, font, label);
            }
            Save(surface, Path.Combine(outDir, $"{map.Row}.png"));
            _output.WriteLine($"{map.Row} ({map.LevelFileName}) {texture.Width}x{texture.Height} <- {map.TexturePath}");
        }
    }

    private static SKBitmap? LoadTexture(
        GameAssetProvider provider, IReadOnlyList<SectorMapInfo> maps, SectorMapFit fit)
    {
        var info = maps.FirstOrDefault(m => m.Row.Equals(fit.PamphletRow, StringComparison.OrdinalIgnoreCase));
        var path = info is null ? null : provider.ExtractTextureByGameRef(info.TexturePath);
        return path is null ? null : SKBitmap.Decode(path);
    }

    private List<(int Variant, double ScaleX, double ScaleY, double OffsetX, double OffsetY, double Score)> SolveFit(
        SKBitmap texture, IReadOnlyList<DoorWorldLocation> cloud, (double L, double T, double R, double B) area)
    {
        var mask = BuildPlanMask(texture, area);
        var maskCells = mask.Count(m => m);
        var areaL = area.L * MaskW;
        var areaR = area.R * MaskW;
        var areaT = area.T * MaskH;
        var areaB = area.B * MaskH;

        // A few thousand points is plenty and keeps the search fast.
        var stride = Math.Max(1, cloud.Count / 3000);
        var sampled = cloud.Where((_, i) => i % stride == 0).ToList();

        var results = new List<(int Variant, double ScaleX, double ScaleY, double OffsetX, double OffsetY, double Score)>();

        for (var variant = 0; variant < 8; variant++)
        {
            var oriented = sampled.Select(p => SectorMapCalibration.ApplyVariant(p.X, p.Y, variant)).ToList();
            var (minX, maxX, minY, maxY) = SectorMapCalibration.CloudBounds(oriented);
            var span = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
            // Work in a unit cloud so the search ranges are level-independent.
            var unit = oriented.Select(p => ((p.X - minX) / span, (p.Y - minY) / span)).ToList();

            // The cloud must land inside the drawing, so it can be neither bigger than that
            // area nor placed outside it.
            var maxSize = Math.Max(areaR - areaL, areaB - areaT);
            double loSize = maxSize * 0.2, hiSize = maxSize * 1.1;
            double loX = areaL - maxSize * 0.1, hiX = areaR;
            double loY = areaT - maxSize * 0.1, hiY = areaB;
            (double Size, double X, double Y, double Score) local = (0, 0, 0, -1);

            for (var pass = 0; pass < 4; pass++)
            {
                var sizeStep = (hiSize - loSize) / 10;
                var xStep = (hiX - loX) / 12;
                var yStep = (hiY - loY) / 12;
                for (var size = loSize; size <= hiSize; size += sizeStep)
                {
                    for (var ox = loX; ox <= hiX; ox += xStep)
                    {
                        for (var oy = loY; oy <= hiY; oy += yStep)
                        {
                            var score = Score(unit, mask, maskCells, size, ox, oy);
                            if (score > local.Score) local = (size, ox, oy, score);
                        }
                    }
                }
                loSize = local.Size - sizeStep; hiSize = local.Size + sizeStep;
                loX = local.X - xStep; hiX = local.X + xStep;
                loY = local.Y - yStep; hiY = local.Y + yStep;
            }

            // Mask cells are square over a non-square texture, so the same world-unit size
            // becomes a different fraction horizontally and vertically.
            var scaleX = local.Size / span / MaskW;
            var scaleY = local.Size / span / MaskH;
            results.Add((variant, scaleX, scaleY,
                local.X / MaskW - minX * scaleX,
                local.Y / MaskH - minY * scaleY,
                local.Score));
        }

        return results;
    }

    /// <summary>
    /// Overlap between the cloud's footprint and the drawing, as intersection over union.
    /// Rewarding only "points on the plan" would shrink the whole level into one room, and
    /// rewarding only spread would sprawl it across the page; IoU punishes both.
    /// </summary>
    private static double Score(
        List<(double X, double Y)> unit, bool[] mask, int maskCells, double size, double ox, double oy)
    {
        if (unit.Count == 0 || maskCells == 0) return 0;

        // Actor positions are points, not fills, so a bare footprint is full of holes and IoU
        // would reward stretching the level until the holes cover the page. Dilating by one
        // cell turns the cloud into the area it actually occupies.
        var footprint = new HashSet<int>();
        foreach (var (u, v) in unit)
        {
            var px = (int)(ox + u * size);
            var py = (int)(oy + v * size);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = px + dx;
                    var ny = py + dy;
                    if (nx < 0 || ny < 0 || nx >= MaskW || ny >= MaskH) continue;
                    footprint.Add(ny * MaskW + nx);
                }
            }
        }
        if (footprint.Count == 0) return 0;

        var intersection = footprint.Count(i => mask[i]);
        return intersection / (double)(footprint.Count + maskCells - intersection);
    }

    /// <summary>The drawing, as a boolean mask over a coarse grid: anything not page background.</summary>
    private static bool[] BuildPlanMask(SKBitmap texture, (double L, double T, double R, double B) area)
    {
        var background = DominantColor(texture);
        var mask = new bool[MaskW * MaskH];
        for (var my = 0; my < MaskH; my++)
        {
            for (var mx = 0; mx < MaskW; mx++)
            {
                var fx = (mx + 0.5) / MaskW;
                var fy = (my + 0.5) / MaskH;
                if (fx < area.L || fx > area.R || fy < area.T || fy > area.B) continue;
                var x = Math.Clamp((int)(fx * texture.Width), 0, texture.Width - 1);
                var y = Math.Clamp((int)(fy * texture.Height), 0, texture.Height - 1);
                var c = texture.GetPixel(x, y);
                mask[my * MaskW + mx] =
                    Math.Abs(c.Red - background.Red)
                  + Math.Abs(c.Green - background.Green)
                  + Math.Abs(c.Blue - background.Blue) > 70;
            }
        }
        return mask;
    }

    private static SKColor DominantColor(SKBitmap bitmap)
    {
        var counts = new Dictionary<int, int>();
        for (var y = 0; y < bitmap.Height; y += 8)
        {
            for (var x = 0; x < bitmap.Width; x += 8)
            {
                var c = bitmap.GetPixel(x, y);
                // Quantize to 16-step buckets so anti-aliased shades pool together.
                var key = (c.Red >> 4 << 8) | (c.Green >> 4 << 4) | (c.Blue >> 4);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
        var top = counts.MaxBy(kv => kv.Value).Key;
        return new SKColor(
            (byte)(((top >> 8) & 0xF) << 4 | 0x8),
            (byte)(((top >> 4) & 0xF) << 4 | 0x8),
            (byte)((top & 0xF) << 4 | 0x8));
    }

    private static void Save(SKSurface surface, string outPath)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(outPath);
        data.SaveTo(fs);
    }

    private static string OutDir(string leaf)
    {
        var dir = Path.Combine(FindRepoRoot(), "tools", "shots", "calib", leaf);
        Directory.CreateDirectory(dir);
        return dir;
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
