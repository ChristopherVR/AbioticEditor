using SkiaSharp;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Projects world coordinates onto the game's drawn sector map textures. The game
/// stores no world-bounds for its maps, so the transform is fitted: the sub-level's
/// full actor cloud (percentile-trimmed against outliers) is mapped onto the map
/// drawing's content rectangle (the largest connected non-background region of the
/// texture - which skips title text and legend). Orientation differs per drawing and
/// is baked in <see cref="VariantFor"/> from visual calibration composites.
/// </summary>
public static class SectorMapCalibration
{
    /// <summary>
    /// Orientation variant per cooked level name: bits 0-1 rotate (X,Y) by 0/90/180/270
    /// degrees, bit 2 flips X first. Values picked by eye from the calibration
    /// composites (tools/shots/calib); unlisted levels use the Office1 default.
    /// </summary>
    private static readonly Dictionary<string, int> Variants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Facility_Office1"] = 4,
    };

    private const int DefaultVariant = 4;

    public static int VariantFor(string? levelFileName)
        => levelFileName is not null && Variants.TryGetValue(levelFileName, out var v) ? v : DefaultVariant;

    /// <summary>Applies an orientation variant to a world position (top-down X/Y).</summary>
    public static (double X, double Y) ApplyVariant(double x, double y, int variant)
    {
        if ((variant & 4) != 0) x = -x;
        return (variant & 3) switch
        {
            1 => (-y, x),
            2 => (-x, -y),
            3 => (y, -x),
            _ => (x, y),
        };
    }

    /// <summary>
    /// The map drawing's content rectangle: bounding box of the largest connected
    /// region of non-background pixels. Background = the color of the texture's
    /// corners; title text and small legend marks form separate smaller components
    /// and are ignored.
    /// </summary>
    public static SKRectI DetectContentRect(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var background = DominantColor(bitmap);

        bool IsContent(int x, int y)
        {
            var c = bitmap.GetPixel(x, y);
            return Math.Abs(c.Red - background.Red)
                 + Math.Abs(c.Green - background.Green)
                 + Math.Abs(c.Blue - background.Blue) > 70;
        }

        // Flood fill over a coarse grid (every pixel is overkill for bbox purposes).
        const int step = 2;
        var cols = (w + step - 1) / step;
        var rows = (h + step - 1) / step;
        var visited = new bool[cols * rows];
        var components = new List<(SKRectI Box, int Count)>();
        var stack = new Stack<(int Cx, int Cy)>();

        for (var cy = 0; cy < rows; cy++)
        {
            for (var cx = 0; cx < cols; cx++)
            {
                if (visited[cy * cols + cx]) continue;
                visited[cy * cols + cx] = true;
                if (!IsContent(Math.Min(cx * step, w - 1), Math.Min(cy * step, h - 1))) continue;

                var minX = cx; var maxX = cx; var minY = cy; var maxY = cy;
                var count = 0;
                stack.Push((cx, cy));
                while (stack.Count > 0)
                {
                    var (px, py) = stack.Pop();
                    count++;
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;

                    foreach (var (nx, ny) in new[] { (px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                        if (visited[ny * cols + nx]) continue;
                        visited[ny * cols + nx] = true;
                        if (IsContent(Math.Min(nx * step, w - 1), Math.Min(ny * step, h - 1)))
                        {
                            stack.Push((nx, ny));
                        }
                    }
                }

                components.Add((new SKRectI(minX * step, minY * step,
                    Math.Min(maxX * step + step, w), Math.Min(maxY * step + step, h)), count));
            }
        }

        // Drop ring-like components (the decorative frame): a bbox spanning nearly the
        // whole image with almost no interior fill. Then take the union of every major
        // component (>= 25% of the biggest) - floor plans are often drawn in pieces.
        var imageArea = (double)w * h;
        var candidates = components.Where(c =>
        {
            var boxArea = (double)c.Box.Width * c.Box.Height;
            var fill = c.Count * (double)(step * step) / Math.Max(1, boxArea);
            var ringLike = boxArea > imageArea * 0.8 && fill < 0.15;
            return !ringLike;
        }).ToList();
        if (candidates.Count == 0) return new SKRectI(0, 0, w, h);

        var biggest = candidates.Max(c => c.Count);
        var union = SKRectI.Empty;
        foreach (var c in candidates.Where(c => c.Count >= biggest * 0.25))
        {
            union = union.IsEmpty ? c.Box : SKRectI.Union(union, c.Box);
        }
        return union.IsEmpty ? new SKRectI(0, 0, w, h) : union;
    }

    /// <summary>
    /// The texture's dominant color over a coarse grid: the page background. Sampling a
    /// corner is wrong for these maps - the decorative frame covers the corners.
    /// </summary>
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

    /// <summary>
    /// Percentile-trimmed bounds of an oriented point cloud (5th-95th), so a handful
    /// of far-away actors (skybox, parked props) can't stretch the fit.
    /// </summary>
    public static (double MinX, double MaxX, double MinY, double MaxY) CloudBounds(
        IReadOnlyList<(double X, double Y)> points)
    {
        var xs = points.Select(p => p.X).OrderBy(v => v).ToArray();
        var ys = points.Select(p => p.Y).OrderBy(v => v).ToArray();
        double Pct(double[] sorted, double pct)
            => sorted[Math.Clamp((int)(pct * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return (Pct(xs, 0.05), Pct(xs, 0.95), Pct(ys, 0.05), Pct(ys, 0.95));
    }

    /// <summary>
    /// Builds the world-to-pixel projector for one map: orient the cloud, fit its
    /// trimmed bounds into the content rect (uniform scale, centered).
    /// </summary>
    public static Func<DoorWorldLocation, (float X, float Y)> BuildProjector(
        IReadOnlyList<DoorWorldLocation> cloud, SKRectI content, int variant)
    {
        var oriented = cloud.Select(p => ApplyVariant(p.X, p.Y, variant)).ToList();
        var (minX, maxX, minY, maxY) = CloudBounds(oriented);
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min(content.Width / spanX, content.Height / spanY);
        var offsetX = content.Left + (content.Width - spanX * scale) / 2.0;
        var offsetY = content.Top + (content.Height - spanY * scale) / 2.0;

        return loc =>
        {
            var (x, y) = ApplyVariant(loc.X, loc.Y, variant);
            return ((float)(offsetX + (x - minX) * scale), (float)(offsetY + (y - minY) * scale));
        };
    }
}
