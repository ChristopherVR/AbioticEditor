using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using SkiaSharp;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class IconFormatProbeTests
{
    private readonly ITestOutputHelper _output;

    public IconFormatProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_CachedIconPixels()
    {
        // Inspect the on-disk PNG we already extracted to determine whether icons
        // really are white-mask-only or whether color data is present.
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticEditor", "assets", "textures",
            "AbioticFactor", "Content", "Textures", "GUI", "ItemIcons",
            "itemicon_armor_chest_groupe.png");

        if (!File.Exists(path))
        {
            _output.WriteLine($"No cached icon at {path}; run the GameAssetProviderTests first.");
            return;
        }

        using var bitmap = SKBitmap.Decode(path);
        _output.WriteLine($"PNG: {bitmap.Width}x{bitmap.Height}  colorType={bitmap.ColorType}  alphaType={bitmap.AlphaType}");

        // Collect channel statistics across all pixels with non-zero alpha
        long rSum = 0, gSum = 0, bSum = 0, aSum = 0, opaque = 0;
        byte rMin = 255, gMin = 255, bMin = 255;
        byte rMax = 0, gMax = 0, bMax = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var c = bitmap.GetPixel(x, y);
                aSum += c.Alpha;
                if (c.Alpha > 8)
                {
                    rSum += c.Red; gSum += c.Green; bSum += c.Blue; opaque++;
                    if (c.Red < rMin) rMin = c.Red; if (c.Red > rMax) rMax = c.Red;
                    if (c.Green < gMin) gMin = c.Green; if (c.Green > gMax) gMax = c.Green;
                    if (c.Blue < bMin) bMin = c.Blue; if (c.Blue > bMax) bMax = c.Blue;
                }
            }
        }
        if (opaque > 0)
        {
            _output.WriteLine($"Opaque samples: {opaque}");
            _output.WriteLine($"  avg color:  R={rSum / opaque} G={gSum / opaque} B={bSum / opaque}");
            _output.WriteLine($"  R range:    {rMin}-{rMax}");
            _output.WriteLine($"  G range:    {gMin}-{gMax}");
            _output.WriteLine($"  B range:    {bMin}-{bMax}");
        }
        _output.WriteLine($"  total samples: {(bitmap.Width / 4) * (bitmap.Height / 4)}");
    }

    [Fact]
    public void Probe_FindMaterials()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        var keys = provider.AssetPaths.ToList();
        // Search for materials that might apply colors to item icons
        var materials = keys
            .Where(k => k.Contains("M_ItemIcon", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("MI_ItemIcon", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("M_Item_", StringComparison.OrdinalIgnoreCase)
                     || (k.EndsWith(".uasset") && k.Contains("Inventory", StringComparison.OrdinalIgnoreCase) && k.Contains("Mat", StringComparison.OrdinalIgnoreCase)))
            .Take(40)
            .ToList();
        _output.WriteLine($"Item icon materials/mat-related: {materials.Count}");
        foreach (var k in materials) _output.WriteLine($"  {k}");

        _output.WriteLine("");
        // Look for materials specific to one of our items (chest_groupe)
        _output.WriteLine("--- All chest/armor-related ---");
        foreach (var k in keys.Where(k => k.EndsWith(".uasset") && k.Contains("groupe", StringComparison.OrdinalIgnoreCase)).Take(20))
        {
            _output.WriteLine($"  {k}");
        }

        _output.WriteLine("");
        // Look for SlateBrush or specific UE inventory widget patterns
        _output.WriteLine("--- Looking for InventoryItemImage / W_InventoryItem ---");
        foreach (var k in keys.Where(k => k.EndsWith(".uasset") && (k.Contains("InventoryItem", StringComparison.OrdinalIgnoreCase) || k.Contains("W_Item", StringComparison.OrdinalIgnoreCase))).Take(20))
        {
            _output.WriteLine($"  {k}");
        }
    }

    [Fact]
    public void Probe_SearchForAlternateIconPaths()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        var keys = provider.AssetPaths.ToList();
        _output.WriteLine($"Total mounted files: {keys.Count}");

        // Look for icon-related files
        var iconKeys = keys
            .Where(k => k.Contains("itemicon", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("/GUI/ItemIcons/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _output.WriteLine($"Icon-related: {iconKeys.Count}");
        foreach (var k in iconKeys.Take(15))
        {
            _output.WriteLine($"  {k}");
        }

        // For ammo_9mm in particular
        _output.WriteLine("");
        _output.WriteLine("--- 9mm-related ---");
        foreach (var k in keys.Where(k => k.Contains("9mm", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset")).Take(15))
        {
            _output.WriteLine($"  {k}");
        }

        // For chest armor
        _output.WriteLine("");
        _output.WriteLine("--- chest_groupe-related ---");
        foreach (var k in keys.Where(k => k.Contains("chest_groupe", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset")).Take(15))
        {
            _output.WriteLine($"  {k}");
        }
    }
}
