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

    /// <summary>
    /// The wiki-style stat block (see <see cref="ItemStats"/>) resolves real numbers for a
    /// weapon, an armor piece and a food item when the game is installed. Values are pinned to
    /// the sledgehammer/lead-vest/pest-goulash rows the item-stats probe confirmed against the
    /// wiki's own published numbers (docs/reference/research/research-wiki-round10.md).
    /// </summary>
    [Fact]
    public void LoadCatalog_ResolvesItemStats_ForWeaponArmorAndFood()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);

        var sledgehammer = catalog.Find("sledgehammer");
        Assert.NotNull(sledgehammer);
        Assert.NotNull(sledgehammer!.Stats);
        var weapon = sledgehammer.Stats!.Weapon;
        Assert.NotNull(weapon);
        Assert.True(weapon!.IsMelee);
        Assert.Equal(45, weapon.DamagePerHit);
        Assert.Equal("rebar", sledgehammer.Stats.Repair?.ItemId);
        Assert.NotNull(sledgehammer.Stats.Salvage);
        Assert.Contains(sledgehammer.Stats.Salvage!.Drops, d => d.ItemId == "woodplank");

        var leadVest = catalog.Find("armor_chest_leadvest");
        Assert.NotNull(leadVest);
        var armor = leadVest!.Stats?.Armor;
        Assert.NotNull(armor);
        Assert.Equal(5, armor!.ArmorBonus);

        var soup = catalog.Find("soup_pestgoulash");
        Assert.NotNull(soup);
        var consumable = soup!.Stats?.Consumable;
        Assert.NotNull(consumable);
        Assert.True(consumable!.HungerFill > 0);
        Assert.True(consumable.ThirstFill > 0);
        Assert.Contains("Buff_SouperSatisfied", consumable.BuffsApplied);

        // A plain resource has no weapon/armor/consumable data, but does carry a repair reference
        // to itself and a salvage entry (it IS its own scrap).
        var scrapMetal = catalog.Find("scrap_metal");
        Assert.NotNull(scrapMetal);
        Assert.Null(scrapMetal!.Stats?.Weapon);
        Assert.Null(scrapMetal.Stats?.Armor);
        Assert.Null(scrapMetal.Stats?.Consumable);
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
