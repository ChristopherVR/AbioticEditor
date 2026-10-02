using AbioticEditor.Core.Items;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Web.Components.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// What the 3D view's inspector offers for a picked object: the items a storage accepts (the game's
/// tag rule), which bench upgrades a bench takes, whether a piece gets the POWER card, and the
/// garden card for a garden plot. Fixture-backed tests skip when the Cascade world is absent.
/// </summary>
public sealed class PlacedObjectRulesTests
{
    // Token streams exactly as the game stores them (version, has-root, expression...).
    private static readonly ItemTagQuery NoPets = new(["Item.Pet"], [0, 1, 3, 1, 0], "NONE( Item.Pet )");
    private static readonly ItemTagQuery AnyFood = new(["Item.Food"], [0, 1, 1, 1, 0], "ANY( Item.Food )");
    private static readonly ItemTagQuery AllFish = new(["Item.Fish"], [0, 1, 2, 1, 0], "ALL( Item.Fish )");

    [Fact]
    public void Tag_query_follows_the_games_container_rules()
    {
        Assert.True(NoPets.Matches(["Item.Resource"]));
        Assert.False(NoPets.Matches(["Item.Pet.Small"]));
        Assert.True(AnyFood.Matches(["Item.Food.Cooked", "Item.Material.Biological"]));
        Assert.False(AnyFood.Matches(["Item.Resource"]));
        Assert.True(AllFish.Matches(["Item.Fish"]));
        Assert.False(AllFish.Matches(["Item.Fishing.Bait"]));
        Assert.False(AllFish.Matches([]));
    }

    [Fact]
    public void Tag_query_handles_nested_and_empty_and_unreadable_streams()
    {
        // ALL_EXPR( ANY(Item.Food), NONE(Item.Pet) )
        var nested = new ItemTagQuery(["Item.Food", "Item.Pet"], [0, 1, 5, 2, 1, 1, 0, 3, 1, 1]);
        Assert.True(nested.Matches(["Item.Food"]));
        Assert.False(nested.Matches(["Item.Food", "Item.Pet"]));
        Assert.False(nested.Matches(["Item.Resource"]));

        Assert.True(new ItemTagQuery([], [0, 0]).IsEmpty);
        Assert.True(new ItemTagQuery([], [0, 0]).Matches(["Anything"]));
        // A stream this reader cannot follow never hides items.
        Assert.True(new ItemTagQuery(["Item.Food"], [0, 1, 9]).Matches(["Item.Resource"]));
    }

    [Fact]
    public void Shared_item_transporter_tag_counts_for_each_of_its_rows()
    {
        Assert.Equal("BenchUpgrade.ItemTransporter", BenchUpgradeCatalog.All.Single(u => u.Row == "ItemTransporter_ChefStation").Tag);
        Assert.True(BenchUpgradeCatalog.IsInstalled(["ItemTransporter"], "ItemTransporter_ChefStation"));
        Assert.True(BenchUpgradeCatalog.IsInstalled(["ItemTransporter"], "ItemTransporter"));
        Assert.False(BenchUpgradeCatalog.IsInstalled(["ItemTransporter"], "Cheffigy"));
    }

    [Fact]
    public void Bench_screen_rules_list_only_the_upgrades_a_bench_offers()
    {
        // The shape the game's upgrade screen has: the Chef Station shows its own two, every other
        // bench the rest. Nothing below names a real bench beyond the test's own data.
        var rules = new BenchUpgradeScreenRules(
            ["TougherBench", "PortalSuppression", "ItemTransporter", "ItemTransporter_ChefStation", "Cheffigy"],
            "Deployed_Bench_CookingStation_C",
            ["TougherBench", "PortalSuppression", "ItemTransporter"],
            ["ItemTransporter_ChefStation", "Cheffigy"]);

        var chef = rules.UpgradesFor(["Deployed_Bench_CookingStation_C", "AbioticDeployed_CraftingBench_ParentBP_C"], ["ItemTransporter"]);
        Assert.Equal(["Cheffigy", "ItemTransporter_ChefStation"], chef.Select(u => u.Row).Order(StringComparer.Ordinal));

        var bench = rules.UpgradesFor(["Deployed_CraftingBench_Default_C", "AbioticDeployed_CraftingBench_ParentBP_C"], []);
        Assert.DoesNotContain(bench, u => u.Row is "Cheffigy" or "ItemTransporter_ChefStation" or "BenchTurret");
        Assert.Contains(bench, u => u.Row == "PortalSuppression");

        // An installed upgrade the screen would not offer stays listed so it can be removed.
        var withTurret = rules.UpgradesFor(["Deployed_CraftingBench_Default_C"], ["BenchTurret"]);
        Assert.Contains(withTurret, u => u.Row == "BenchTurret");
    }

    [Fact]
    public void Chef_station_item_transporter_writes_the_shared_tag()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var bench = WorldMapAccessor.Entries(save, "DeployedObjectMap")
            .First(e => BenchUpgradeCatalog.SupportsUpgrades(e.Props) && !BenchUpgradeCatalog.ReadInstalledRows(e.Props).Contains("ItemTransporter"));

        Assert.True(BenchUpgradeCatalog.SetInstalled(bench.Props, "ItemTransporter_ChefStation", true));
        Assert.Contains("ItemTransporter", BenchUpgradeCatalog.ReadInstalledRows(bench.Props));
        Assert.DoesNotContain("ItemTransporter_ChefStation", BenchUpgradeCatalog.ReadInstalledRows(bench.Props));
        // Already there through the shared tag.
        Assert.False(BenchUpgradeCatalog.SetInstalled(bench.Props, "ItemTransporter", true));
        Assert.True(BenchUpgradeCatalog.SetInstalled(bench.Props, "ItemTransporter_ChefStation", false));
        Assert.DoesNotContain("ItemTransporter", BenchUpgradeCatalog.ReadInstalledRows(bench.Props));
    }

    [Fact]
    public void Garden_plot_gets_its_garden_card_with_what_is_growing()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var session = new WorldSaveSession(data, path);
        var planted = new GardenPlotsFeature().Read(data.Raw)
            .First(e => e.Fields.Any(f => f.Id.StartsWith("crop:", StringComparison.Ordinal)));

        var cards = PlacedFeatureCard3D.FeaturesFor(session, planted.Key);
        var garden = Assert.Single(cards, c => c.Feature is GardenPlotsFeature);
        Assert.Contains(garden.Entry.Fields, f => f.Id.StartsWith("crop:", StringComparison.Ordinal) && f.Options is { Count: > 0 });
        // Its crop choices are plantable crop rows only, never the whole item list.
        var crop = garden.Entry.Fields.First(f => f.Id.StartsWith("crop:", StringComparison.Ordinal));
        Assert.All(crop.Options!, o => Assert.True(o.StartsWith("Plant_", StringComparison.Ordinal) || o == GardenPlotsFeature.EmptyOption));

        // A plain crate has no such card.
        var crate = session.Containers.First(c => c.ClassName?.Contains("StorageCrate", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(PlacedFeatureCard3D.FeaturesFor(session, crate.Id));
    }

    [Fact]
    public void Power_card_only_for_pieces_that_take_or_give_power()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var session = new WorldSaveSession(data, path);
        var gardenKey = new GardenPlotsFeature().Read(data.Raw)[0].Key;

        // No game answer and no garden plot plugged anywhere: no POWER card.
        Assert.False(session.IsPowerDevice(gardenKey, null));
        // The game says it runs on power: the card is offered.
        Assert.True(session.IsPowerDevice(gardenKey, true));

        // Something plugged in always gets it, whatever the class data says.
        var plugged = WorldMapAccessor.Entries(data.Raw, "PowerSocketMap")
            .Select(e => e.Props.GetString("PluggedInDeviceAssetID_"))
            .FirstOrDefault(d => d is { Length: 32 });
        if (plugged is not null) Assert.True(session.IsPowerDevice(plugged, false));
    }
}
