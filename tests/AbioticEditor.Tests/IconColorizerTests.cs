using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class IconColorizerTests
{
    private readonly ITestOutputHelper _output;

    public IconColorizerTests(ITestOutputHelper output)
    {
        _output = output;
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
