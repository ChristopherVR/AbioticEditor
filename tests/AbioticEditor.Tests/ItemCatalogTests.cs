using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class ItemCatalogTests
{
    private readonly ITestOutputHelper _output;

    public ItemCatalogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadCatalog_ResolvesKnownItems()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);

        Assert.True(catalog.Count > 1000, $"expected >1000 items, got {catalog.Count}");

        var chestArmor = catalog.Find("armor_chest_groupe");
        Assert.NotNull(chestArmor);
        _output.WriteLine($"armor_chest_groupe: {chestArmor!.DisplayName}");
        _output.WriteLine($"  stack={chestArmor.StackSize} dur={chestArmor.MaxDurability} weapon={chestArmor.IsWeapon} weight={chestArmor.Weight:F2}");
        _output.WriteLine($"  icon={chestArmor.IconAssetPath}");
        _output.WriteLine($"  tags=[{string.Join(", ", chestArmor.Tags.Take(5))}]");

        var nineMm = catalog.Find("ammo_9mm");
        Assert.NotNull(nineMm);
        _output.WriteLine($"ammo_9mm: '{nineMm!.DisplayName}' stack={nineMm.StackSize}");

        var glowtulip = catalog.Find("glowtulip");
        Assert.NotNull(glowtulip);
        _output.WriteLine($"glowtulip: '{glowtulip!.DisplayName}'");

        // Spot check description is populated for at least one common item
        Assert.False(string.IsNullOrEmpty(chestArmor.Description),
            $"expected chest armor description, got null/empty");
    }

    [Fact]
    public void ExtractIconByGameRef_ProducesValidPng()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        var entry = catalog.Find("armor_chest_groupe");
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrEmpty(entry!.IconAssetPath));

        var png = provider.ExtractTextureByGameRef(entry.IconAssetPath);
        Assert.NotNull(png);
        Assert.True(File.Exists(png));
        _output.WriteLine($"Icon PNG: {png} ({new FileInfo(png!).Length:N0} bytes)");
    }
}
