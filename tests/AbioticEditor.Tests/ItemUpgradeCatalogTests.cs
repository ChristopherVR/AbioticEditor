using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// The upgrade graph from the bundled game data. The drills, the construction gauntlet and the
/// flashlight weapons used to be crafted from their own recipes; the game now makes them only by
/// upgrading, so upgrade and downgrade must walk the real item chain.
/// </summary>
public sealed class ItemUpgradeCatalogTests
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

        // A row keyed by a recipe id is not an item, so the live graph never offers it.
        Assert.Null(catalog.UpgradeFor("recipe_makeshift_screwdriver"));
        Assert.Equal("screwdriver", catalog.SourceOf("handdrill")?.SourceId);
    }

    private static GameDataRegistry Registry()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "assets", "registry", "registry.json");
            if (File.Exists(path)) return GameDataRegistry.TryLoad(path) ?? throw new InvalidDataException(path);
        }
        throw new FileNotFoundException("assets/registry/registry.json");
    }

    private static ItemUpgradeCatalog Catalog()
    {
        var registry = Registry();
        return ItemUpgradeCatalog.FromRegistry(registry.ItemUpgrades!, registry.Items!.Select(item => item.Id));
    }

    [Theory]
    [InlineData("screwdriver", "handdrill")]
    [InlineData("handdrill", "powerdrill")]
    [InlineData("powerdrill", "constructiongauntlet")]
    [InlineData("scrapshot", "scrapshot_flashlight")]
    [InlineData("pistol_security", "pistol_security_flashlight")]
    public void Tool_and_flashlight_chains_upgrade_and_downgrade_through_real_items(string source, string output)
    {
        var catalog = Catalog();
        Assert.Equal(output, catalog.UpgradeFor(source)?.OutputId);
        Assert.Equal(source, catalog.SourceOf(output)?.SourceId);
    }

    [Fact]
    public void A_game_row_keyed_by_a_recipe_never_becomes_an_item()
    {
        var registry = Registry();
        Assert.Contains(registry.ItemUpgrades!, upgrade => upgrade.SourceId == "recipe_makeshift_screwdriver");

        var catalog = Catalog();
        var items = registry.Items!.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Null(catalog.UpgradeFor("recipe_makeshift_screwdriver"));
        Assert.All(catalog.Upgrades, upgrade =>
        {
            Assert.Contains(upgrade.SourceId, items);
            Assert.Contains(upgrade.OutputId, items);
        });

        // Without an item list (an old host) the first producer still wins, which is the real one.
        Assert.Equal("screwdriver", ItemUpgradeCatalog.FromRegistry(registry.ItemUpgrades!).SourceOf("handdrill")?.SourceId);
    }

    [Fact]
    public void Downgrading_a_hand_drill_in_a_save_gives_back_a_screwdriver()
    {
        var data = PlayerSaveReader.ReadFromFile(CommunityUpdateFiveTests.Fixture("PlayerData/Player_76561197993781479.sav"));
        var session = new PlayerSaveSession(data, "Player_76561197993781479.sav", itemUpgrades: Catalog());
        Assert.True(session.TryGetInventorySlot(PlayerInventoryArea.Backpack, 0, out var slot));
        Assert.True(session.TrySetInventorySlot(PlayerInventoryArea.Backpack, 0, slot with { ItemId = "handdrill", Count = 1 }));

        Assert.True(session.TryApplyItemUpgrade(PlayerInventoryArea.Backpack, 0, downgrade: true));
        Assert.True(session.TryGetInventorySlot(PlayerInventoryArea.Backpack, 0, out var downgraded));
        Assert.Equal("screwdriver", downgraded.ItemId);

        Assert.True(session.TryApplyItemUpgrade(PlayerInventoryArea.Backpack, 0, downgrade: false));
        Assert.True(session.TryApplyItemUpgrade(PlayerInventoryArea.Backpack, 0, downgrade: false));
        Assert.True(session.TryGetInventorySlot(PlayerInventoryArea.Backpack, 0, out var upgraded));
        Assert.Equal("powerdrill", upgraded.ItemId);
    }
}
