using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;
using SkiaSharp;

namespace AbioticEditor.Tests;

public class IconColorizerTests
{
    private readonly ITestOutputHelper _output;

    public IconColorizerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Full_color_art_survives_while_legacy_masks_keep_their_silhouette(bool fullColor)
    {
        var directory = Directory.CreateTempSubdirectory("abiotic-icon-format-");
        try
        {
            var path = Path.Combine(directory.FullName, "icon.png");
            using (var bitmap = new SKBitmap(16, 16))
            {
                bitmap.Erase(fullColor ? SKColors.Transparent : SKColors.Black);
                for (var y = 4; y < 12; y++)
                    for (var x = 4; x < 12; x++) bitmap.SetPixel(x, y, fullColor ? SKColors.Red : SKColors.Transparent);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, png.ToArray());
            }
            var item = new ItemCatalogEntry("test", "Test", null, null, 1, 1, false, 0, []);
            var output = IconColorizer.Colorize(path, item);
            using var result = SKBitmap.Decode(output);
            Assert.Equal((byte)0, result.GetPixel(0, 0).Alpha);
            Assert.Equal((byte)255, result.GetPixel(8, 8).Alpha);
            if (fullColor)
            {
                Assert.Equal(path, output);
                Assert.Equal(SKColors.Red, result.GetPixel(8, 8));
            }
            else Assert.NotEqual(path, output);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Colorize_ProducesColoredPngsForRepresentativeItems()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);

        // Spread across categories so visual inspection is meaningful
        string[] ids =
        {
            "armor_chest_groupe",   // gear
            "knife_super",          // weapon
            "ammo_9mm",             // ammo
            "glowtulip",            // plant
            "personalteleporter",   // tech / teleporter
            "key_security",         // key
            "food_rotten",          // food
            "scrap_metal",          // metal
        };

        foreach (var id in ids)
        {
            var entry = catalog.Find(id);
            if (entry?.IconAssetPath is null) { _output.WriteLine($"  {id}: no catalog entry"); continue; }

            var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
            if (raw is null) { _output.WriteLine($"  {id}: no raw icon"); continue; }

            var colored = IconColorizer.Colorize(raw, entry);
            _output.WriteLine($"  {id}: {colored}  ({entry.DisplayName})");
            Assert.True(File.Exists(colored));
        }
    }
}
