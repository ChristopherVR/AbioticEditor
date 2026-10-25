using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class ItemAcquisitionIndexTests
{
    [Fact]
    public void Sources_are_a_case_insensitive_union_of_explicit_item_acquisition_routes()
    {
        var fish = new FishDefinition("fish_row", "resource", false, Location: "Known water");
        var index = new ItemAcquisitionIndex([Item("scrappable", new SalvageDrop("RESOURCE", 1, 2, 0.5))],
            [Trader("merchant", [new("Resource", 3, "stock_flag")], [new("payment", 4, null)])], [fish]);

        var routes = index.SourcesFor("rEsOuRcE");

        Assert.Equal(3, routes.Count);
        Assert.Equal(3, routes.Select(route => route.Kind).Distinct().Count());
        Assert.Equal("scrappable", Assert.Single(routes, route => route.Kind == ItemAcquisitionKind.Salvage).SalvageSourceItemId);
        Assert.Equal("Known water", Assert.Single(routes, route => route.Kind == ItemAcquisitionKind.Fishing).Fish!.Location);
        Assert.Equal("stock_flag", Assert.Single(routes, route => route.Kind == ItemAcquisitionKind.Trade).Offer!.RequiredFlag);
    }

    [Fact]
    public void Trader_accepted_items_are_a_separate_list_and_are_not_assumed_to_be_a_price_pair()
    {
        var index = new ItemAcquisitionIndex([], [Trader("merchant",
            [new("first", 2, null), new("second", 1, null)],
            [new("payment_a", 4, null), new("PAYMENT_A", 4, null), new("payment_b", 7, null),
                new("payment_c", 1, null), new("invalid_payment", 0, null)])]);

        var first = Assert.Single(index.SourcesFor("first"));
        var second = Assert.Single(index.SourcesFor("second"));
        Assert.Equal(2, first.Offer!.Count);
        Assert.Equal(3, first.AcceptedItems.Count);
        Assert.Equal(first.AcceptedItems, second.AcceptedItems);
        Assert.Empty(index.SourcesFor("payment_a"));
    }

    [Fact]
    public void Duplicate_catalog_rows_are_deduplicated_without_losing_distinct_source_choices()
    {
        var item = Item("scrappable", new SalvageDrop("resource", 1, 2, 1));
        var trader = Trader("merchant", [new("resource", 1, null), new("RESOURCE", 1, null)], []);
        var fish = new FishDefinition("fish_row", "resource", false);
        var index = new ItemAcquisitionIndex([item, item with { Id = "SCRAPPABLE" }, Item("other", new SalvageDrop("resource", 1, 2, 1))],
            [trader, trader with { Id = "MERCHANT" }], [fish, fish with { Id = "FISH_ROW", ItemId = "RESOURCE" }]);

        Assert.Equal(4, index.SourcesFor("resource").Count);
        Assert.Equal(2, index.SourcesFor("resource").Count(route => route.Kind == ItemAcquisitionKind.Salvage));
    }

    [Fact]
    public void Alternative_fishing_conditions_for_the_same_item_remain_separate()
    {
        var fish = new FishDefinition("fish_row", "resource", false, "Known water", RequiredWorldFlag: "story",
            RequiredDlcId: "dlc", RequiredBaitTag: "bait", NoonMult: 0);
        var index = new ItemAcquisitionIndex([], fish: [fish, fish with { NoonMult = 2 }, fish with { Location = "Other water" }]);

        var routes = index.SourcesFor("resource");
        Assert.Equal(3, routes.Count);
        Assert.All(routes, route =>
        {
            Assert.Equal("story", route.Fish!.RequiredWorldFlag);
            Assert.Equal("dlc", route.Fish.RequiredDlcId);
            Assert.Equal("bait", route.Fish.RequiredBaitTag);
        });
    }

    [Fact]
    public void Missing_or_group_ids_do_not_turn_into_invented_sources()
    {
        var index = new ItemAcquisitionIndex([Item("scrappable", new("AnyMetal", 1, 1, 1), new("None", 1, 1, 1))],
            [Trader("merchant", [new("AnyGlue", 1, null), new("", 1, null)], [])],
            [new("fish_row", null, false), new("other", "AnyFish", false)]);

        Assert.Empty(index.SourcesFor("AnyMetal"));
        Assert.Empty(index.SourcesFor("None"));
        Assert.Empty(index.SourcesFor("AnyGlue"));
        Assert.Empty(index.SourcesFor("AnyFish"));
        Assert.Empty(index.SourcesFor("unknown"));
        Assert.Empty(new ItemAcquisitionIndex(null).SourcesFor("unknown"));
    }

    [Fact]
    public void Malformed_quantities_and_nonfinite_or_zero_chances_are_excluded()
    {
        var index = new ItemAcquisitionIndex([Item("scrappable",
            new("negative", -1, 1, 1), new("empty", 0, 0, 1), new("reversed", 3, 2, 1),
            new("never", 1, 1, 0), new("not_a_number", 1, 1, double.NaN), new("infinite", 1, 1, double.PositiveInfinity),
            new("possible_zero", 0, 2, 0.1), new("raw_percentage", 1, 1, 100))],
            [Trader("merchant", [new("negative", -2, null), new("empty", 0, null)], [new("bad_payment", 0, null)])]);

        foreach (var id in new[] { "negative", "empty", "reversed", "never", "not_a_number", "infinite" })
            Assert.Empty(index.SourcesFor(id));
        Assert.Equal(0, Assert.Single(index.SourcesFor("possible_zero")).Salvage!.QuantityMin);
        Assert.Equal(100, Assert.Single(index.SourcesFor("raw_percentage")).Salvage!.ChanceToDrop);
    }

    [Fact]
    public void Salvage_is_not_reverse_crafting_and_does_not_treat_repair_materials_as_outputs()
    {
        var repaired = Item("repairable") with { Stats = new ItemStats(Repair: new("resource", 1, 2)) };
        var plain = Item("plain");
        var index = new ItemAcquisitionIndex([repaired, plain]);

        Assert.Empty(index.SourcesFor("resource"));
        Assert.Empty(index.SourcesFor("repairable"));
        Assert.Empty(index.SourcesFor("plain"));
    }

    [Fact]
    public void First_catch_recipe_grants_do_not_claim_that_the_recipe_or_its_output_is_obtained()
    {
        var fish = new FishDefinition("fish_row", "fish_item", false, UnlockRecipeId: "recipe_bait");
        var index = new ItemAcquisitionIndex([], fish: [fish]);

        Assert.Single(index.SourcesFor("fish_item"));
        Assert.Empty(index.SourcesFor("recipe_bait"));
        Assert.Empty(index.SourcesFor("bait"));
    }

    [Fact]
    public void Trader_flags_and_unknown_payment_information_are_preserved_without_claiming_access()
    {
        var trader = Trader("merchant", [new("resource", 2, "stock_flag")], []) with { RequiredFlags = ["region_flag"] };
        var route = Assert.Single(new ItemAcquisitionIndex([], [trader]).SourcesFor("resource"));

        Assert.Equal("region_flag", Assert.Single(route.Trader!.RequiredFlags));
        Assert.Equal("stock_flag", route.Offer!.RequiredFlag);
        Assert.Empty(route.AcceptedItems);
    }

    private static ItemCatalogEntry Item(string id, params SalvageDrop[] drops)
        => new(id, id, null, null, 1, 0, false, 0, [])
        {
            Stats = drops.Length == 0 ? null : new ItemStats(Salvage: new(drops)),
        };

    private static TraderInfo Trader(string id, IReadOnlyList<TraderOffer> sells, IReadOnlyList<TraderOffer> accepts)
        => new(id, null, [], sells, accepts);
}
