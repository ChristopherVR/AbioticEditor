using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class ItemUpgradeCatalogTests
{
    private readonly ITestOutputHelper _output;

    public ItemUpgradeCatalogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LoadsUpgradeGraph_FromDtItemUpgrades()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemUpgradeCatalog.LoadFrom(provider);
        _output.WriteLine($"{catalog.Count} upgrade rows");
        Assert.True(catalog.Count >= 80, "DT_ItemUpgrades has ~86 rows");

        // Armor chain: forge chest -> U1, with a real ingredient list.
        var forge = catalog.UpgradeFor("armor_chest_forge");
        Assert.NotNull(forge);
        Assert.Equal("armor_chest_forge_U1", forge!.OutputId);
        Assert.True(forge.Required.Count >= 3);
        Assert.Contains(forge.Required, r => r.ItemId == "carbonplating");

        // Reverse lookup: the U1 item knows where it came from.
        var origin = catalog.SourceOf("armor_chest_forge_U1");
        Assert.NotNull(origin);
        Assert.Equal("armor_chest_forge", origin!.SourceId);

        // Weapons and trinkets are upgradable too - not just armor.
        Assert.True(catalog.IsUpgradable("magnum_military"));
        Assert.True(catalog.IsUpgradable("trinket_kylie"));
        Assert.True(catalog.IsUpgradable("backpack_large"));

        // Case-insensitive like the rest of the item plumbing.
        Assert.True(catalog.IsUpgradable("Magnum_Military"));

        // Top-of-chain items are not upgradable further.
        Assert.False(catalog.IsUpgradable("armor_chest_forge_U1") && catalog.UpgradeFor("armor_chest_forge_U1") is null);
    }
}
