using SkiaSharp;

namespace AbioticEditor.Core.Items;

/// <summary>
/// AF's item icons are stored as alpha-mask-only textures (RGB ≈ 0); the in-game UI
/// material applies color. To keep the editor visually informative we apply a categorical
/// tint based on item tags / id heuristics and bake the result back to PNG.
/// </summary>
public static class IconColorizer
{
    /// <summary>
    /// Applies a tint derived from <paramref name="entry"/> to the mask PNG at
    /// <paramref name="maskPngPath"/> and writes the colorized result alongside it,
    /// returning the colorized path. If colorization fails for any reason the original
    /// mask path is returned.
    /// </summary>
    public static string Colorize(string maskPngPath, ItemCatalogEntry entry)
        => ColorizeCore(maskPngPath, PickTint(entry), ".colored.png");

    /// <summary>
    /// Colorizes a mask PNG with a caller-chosen tint - used for non-item masks like the
    /// customization preview icons, which share the same inverted-alpha format.
    /// </summary>
    public static string ColorizeWithTint(string maskPngPath, byte r, byte g, byte b)
        => ColorizeCore(maskPngPath, new SKColor(r, g, b), $".tint{r:X2}{g:X2}{b:X2}.png");

    private static string ColorizeCore(string maskPngPath, SKColor tint, string outSuffix)
    {
        try
        {
            var outPath = Path.Combine(
                Path.GetDirectoryName(maskPngPath)!,
                Path.GetFileNameWithoutExtension(maskPngPath) + outSuffix);

            if (File.Exists(outPath)) return outPath;

            // Decode the mask into raw, unpremultiplied RGBA bytes so we can iterate cleanly.
            using var srcImage = SKImage.FromEncodedData(maskPngPath);
            if (srcImage is null) return maskPngPath;

            var w = srcImage.Width;
            var h = srcImage.Height;
            var srcInfo = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var srcPixels = new byte[w * h * 4];
            unsafe
            {
                fixed (byte* p = srcPixels)
                {
                    if (!srcImage.ReadPixels(srcInfo, (IntPtr)p, w * 4, 0, 0))
                    {
                        return maskPngPath;
                    }
                }
            }

            // Build a vertical gradient + soft top-left highlight, masked by the source alpha.
            var top = tint;
            var bottom = Multiply(tint, 0.55f);
            var highlight = Lighten(tint, 0.40f);
            var cx = w * 0.35f;
            var cy = h * 0.28f;
            var radius = w * 0.55f;

            var dstPixels = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
            {
                var tY = (float)y / Math.Max(1, h - 1);
                var rowR = top.Red   + (bottom.Red   - top.Red)   * tY;
                var rowG = top.Green + (bottom.Green - top.Green) * tY;
                var rowB = top.Blue  + (bottom.Blue  - top.Blue)  * tY;

                for (var x = 0; x < w; x++)
                {
                    var idx = (y * w + x) * 4;
                    // AF icons are stored INVERTED: alpha=0 inside the silhouette,
                    // alpha=255 (with RGB=0) for the background. The visible icon shape is
                    // where the source is transparent. Treat 255 - srcAlpha as the
                    // silhouette mask.
                    var srcA = srcPixels[idx + 3];
                    var maskA = (byte)(255 - srcA);
                    if (maskA == 0) continue;

                    var dx = x - cx;
                    var dy = y - cy;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    var hl = Math.Clamp(1f - dist / radius, 0f, 1f);
                    hl *= hl;

                    var r = (byte)Math.Clamp(rowR + (highlight.Red   - rowR) * hl * 0.6f, 0, 255);
                    var g = (byte)Math.Clamp(rowG + (highlight.Green - rowG) * hl * 0.6f, 0, 255);
                    var b = (byte)Math.Clamp(rowB + (highlight.Blue  - rowB) * hl * 0.6f, 0, 255);

                    dstPixels[idx + 0] = r;
                    dstPixels[idx + 1] = g;
                    dstPixels[idx + 2] = b;
                    dstPixels[idx + 3] = maskA;
                }
            }

            unsafe
            {
                fixed (byte* p = dstPixels)
                {
                    using var pixmap = new SKPixmap(srcInfo, (IntPtr)p, w * 4);
                    using var imageOut = SKImage.FromPixelCopy(pixmap);
                    using var data = imageOut.Encode(SKEncodedImageFormat.Png, 95);
                    using var stream = File.Create(outPath);
                    data.SaveTo(stream);
                }
            }

            return outPath;
        }
        catch
        {
            return maskPngPath;
        }
    }

    private static SKColor PickTint(ItemCatalogEntry entry)
    {
        // 1) Tag-based priorities - most specific first.
        var tags = string.Join(' ', entry.Tags).ToLowerInvariant();
        var id = entry.Id.ToLowerInvariant();

        foreach (var (needle, color) in TagPalette)
        {
            if (tags.Contains(needle) || id.Contains(needle)) return color;
        }

        // 2) IsWeapon flag is the most reliable fallback
        if (entry.IsWeapon) return MetalGrey;

        return Default;
    }

    // AF-inspired palette (warm-leaning, fitting the dark UI background).
    // NOTE: declared BEFORE TagPalette so the array initializer sees real values
    // and not default-zero SKColors.
    private static readonly SKColor Default      = new(170, 156, 130);  // tan
    private static readonly SKColor MetalGrey    = new(168, 168, 175);
    private static readonly SKColor WeaponOrange = new(214, 122, 50);
    private static readonly SKColor ArmorBlue    = new(110, 142, 180);
    private static readonly SKColor AmmoBrass    = new(204, 162, 80);
    private static readonly SKColor Medical      = new(220, 90, 90);
    private static readonly SKColor PlantGreen   = new(112, 168, 76);
    private static readonly SKColor FoodBrown    = new(184, 124, 76);
    private static readonly SKColor FishBlue     = new(108, 162, 188);
    private static readonly SKColor WaterBlue    = new(96, 152, 188);
    private static readonly SKColor TechCyan     = new(86, 168, 196);
    private static readonly SKColor LaserPurple  = new(170, 96, 196);
    private static readonly SKColor WoodBrown    = new(150, 112, 70);
    private static readonly SKColor PlasticTeal  = new(110, 174, 174);
    private static readonly SKColor ClothTan     = new(204, 184, 142);
    private static readonly SKColor LeatherBrown = new(132, 92, 56);
    private static readonly SKColor BoneCream    = new(232, 220, 188);
    private static readonly SKColor Blood        = new(160, 56, 56);
    private static readonly SKColor KeyGold      = new(220, 180, 80);

    // Ordered: longer / more-specific needles first so they beat broader ones.
    private static readonly (string Needle, SKColor Color)[] TagPalette =
    {
        ("ammo",          AmmoBrass),
        ("magazine",      AmmoBrass),
        ("medkit",        Medical),
        ("medical",       Medical),
        ("bandage",       Medical),
        ("syringe",       Medical),
        ("plant",         PlantGreen),
        ("seed",          PlantGreen),
        ("glowtulip",     PlantGreen),
        ("flora",         PlantGreen),
        ("food",          FoodBrown),
        ("meat",          FoodBrown),
        ("cooked",        FoodBrown),
        ("fish",          FishBlue),
        ("water",         WaterBlue),
        ("liquid",        WaterBlue),
        ("bottle",        WaterBlue),
        ("electronic",    TechCyan),
        ("battery",       TechCyan),
        ("tech",          TechCyan),
        ("circuit",       TechCyan),
        ("computer",      TechCyan),
        ("laser",         LaserPurple),
        ("plasma",        LaserPurple),
        ("anteverse",     LaserPurple),
        ("crystal",       LaserPurple),
        ("metal",         MetalGrey),
        ("scrap",         MetalGrey),
        ("wood",          WoodBrown),
        ("plastic",       PlasticTeal),
        ("cloth",         ClothTan),
        ("leather",       LeatherBrown),
        ("paper",         ClothTan),
        ("bone",          BoneCream),
        ("skull",         BoneCream),
        ("gib",           Blood),
        ("blood",         Blood),
        ("key",           KeyGold),
        ("trinket",       KeyGold),
        ("backpack",      LeatherBrown),
        ("armor",         ArmorBlue),
        ("helmet",        ArmorBlue),
        ("shield",        ArmorBlue),
        ("gear",          ArmorBlue),
        ("weapon",        WeaponOrange),
        ("knife",         WeaponOrange),
        ("club",          WoodBrown),
        ("gun",           WeaponOrange),
        ("rifle",         WeaponOrange),
        ("pistol",        WeaponOrange),
        ("magnum",        WeaponOrange),
        ("shotgun",       WeaponOrange),
        ("crossbow",      WeaponOrange),
        ("teleporter",    LaserPurple),
        ("watch",         KeyGold),
        ("headlamp",      TechCyan),
        ("walkietalkie",  TechCyan),
    };

    private static SKColor Multiply(SKColor c, float k)
        => new((byte)(c.Red * k), (byte)(c.Green * k), (byte)(c.Blue * k), c.Alpha);

    private static SKColor Lighten(SKColor c, float k)
        => new(
            (byte)Math.Clamp(c.Red + 255 * k, 0, 255),
            (byte)Math.Clamp(c.Green + 255 * k, 0, 255),
            (byte)Math.Clamp(c.Blue + 255 * k, 0, 255),
            c.Alpha);
}
