using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class InventoryDismantleServiceTests
{
    [Fact]
    public void Dismantle_plan_previews_and_applies_catalog_backed_ingredients()
    {
        var source = Edit(0, "Workbench", 1);
        var empty = Edit(1, PlayerSaveWriter.EmptySlotRowName, 0);
        var recipes = new[] { new RecipeInfo("recipe_workbench", "Workbench", 1, null, "Crafting",
            [new RecipeIngredient("Metal", 3), new RecipeIngredient("Wood", 2)]) };
        var catalog = ItemCatalog.FromRegistry([
            new ItemCatalogEntry("Metal", "Metal Scrap", null, null, 64, 25, false, 0, []),
            new ItemCatalogEntry("Wood", "Wood Plank", null, null, 32, 10, false, 0, [])],
            new Dictionary<string, string>());
        var service = new InventoryDismantleService(recipes, catalog);

        Assert.True(service.TryPlan(source, [source, empty], reuseSource: true, out var plan, out var preview));
        Assert.Contains("3x Metal Scrap", preview);
        Assert.NotNull(plan);
        InventoryDismantleService.Apply(plan!);

        Assert.Equal("Metal", source.ItemId);
        Assert.Equal(3, source.Count);
        Assert.Equal(25, source.Durability);
        Assert.False(string.IsNullOrWhiteSpace(source.AssetId));
        Assert.Equal("Wood", empty.ItemId);
        Assert.Equal(2, empty.Count);
    }

    [Fact]
    public void Equipment_dismantle_requires_backpack_capacity_and_clears_source()
    {
        var source = Edit(12, "Helmet", 1);
        var backpack = Edit(0, PlayerSaveWriter.EmptySlotRowName, 0);
        var recipes = new[] { new RecipeInfo("recipe_helmet", "Helmet", 1, null, "Crafting",
            [new RecipeIngredient("Metal", 1)]) };
        var service = new InventoryDismantleService(recipes, ItemCatalog.FromRegistry([], new Dictionary<string, string>()));

        Assert.True(service.TryPlan(source, [backpack], reuseSource: false, out var plan, out _));
        InventoryDismantleService.Apply(plan!);

        Assert.True(source.IsEmpty);
        Assert.Equal("Metal", backpack.ItemId);
    }

    private static PlayerInventorySlotEdit Edit(int index, string item, int count) => new(
        new InventoryItemSlot(index, item, count, 0, 0, 0, 0, null, false, null, null));
}
