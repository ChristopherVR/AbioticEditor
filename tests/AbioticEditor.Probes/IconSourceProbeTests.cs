using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probes whether AF stores colored icons anywhere - thumbnails baked into uassets,
/// alternate textures, or material-bound base colors.
/// </summary>
public class IconSourceProbeTests
{
    private readonly ITestOutputHelper _output;
    public IconSourceProbeTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void Inspect_IconUassetMetadata()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var path = "AbioticFactor/Content/Textures/GUI/ItemIcons/itemicon_armor_chest_groupe";
        try
        {
            var pkg = provider.GetType()
                .GetMethod("LoadPackageInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                !.Invoke(provider, new object[] { path });
            var exports = (System.Collections.IEnumerable)pkg!.GetType().GetMethod("GetExports")!.Invoke(pkg, null)!;

            foreach (var e in exports)
            {
                _output.WriteLine($"export: {e.GetType().Name}");
                if (e is UTexture2D tex)
                {
                    _output.WriteLine($"  Format={tex.Format}  sRGB={tex.SRGB}  CompressionSettings={tex.CompressionSettings}");
                    _output.WriteLine($"  Filter={tex.Filter}  LODGroup={tex.LODGroup}");
                    _output.WriteLine($"  Properties:");
                    foreach (var p in tex.Properties)
                    {
                        var v = p.Tag?.GenericValue?.ToString();
                        if (v?.Length > 100) v = v[..100] + "…";
                        _output.WriteLine($"    {p.Name}: {v}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void Search_ForColoredItemTextures()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        var keys = provider.AssetPaths.ToList();

        // Look for textures that might be base-color/diffuse for items
        _output.WriteLine("--- T_*Color, T_*BaseColor, T_*Diffuse under Items ---");
        foreach (var k in keys.Where(k =>
            k.Contains("/Models/Items/", StringComparison.OrdinalIgnoreCase) &&
            k.EndsWith(".uasset") &&
            (k.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("Diffuse", StringComparison.OrdinalIgnoreCase) ||
             k.Contains("Albedo", StringComparison.OrdinalIgnoreCase))).Take(20))
        {
            _output.WriteLine($"  {k}");
        }

        // Maybe under a thumbnails folder?
        _output.WriteLine("");
        _output.WriteLine("--- Anything thumbnail-related ---");
        foreach (var k in keys.Where(k => k.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)).Take(20))
        {
            _output.WriteLine($"  {k}");
        }

        // Look at all chest-armor related textures
        _output.WriteLine("");
        _output.WriteLine("--- All T_*chest* / chest*-related textures ---");
        foreach (var k in keys.Where(k =>
            k.EndsWith(".uasset") &&
            (k.Contains("/Textures/", StringComparison.OrdinalIgnoreCase) || k.Contains("/Models/", StringComparison.OrdinalIgnoreCase)) &&
            k.Contains("chest", StringComparison.OrdinalIgnoreCase)).Take(20))
        {
            _output.WriteLine($"  {k}");
        }
    }
}
