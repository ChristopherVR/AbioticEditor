using AbioticEditor.Core.Items;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class ItemCatalogServiceTests
{
    [Theory]
    [InlineData("weapon_shotgun", "Item.Weapon.Ranged", "weapons")]
    [InlineData("helmet_radiation", "Item.Gear.Head", "armor")]
    [InlineData("firstaid_bandage", "Item.Consumable", "medical")]
    [InlineData("scrap_metal", "Item.Material.Metal", "resources")]
    [InlineData("gardenplot_small", "Item.Deployable", "farming")]
    public void Category_classifier_matches_the_native_palette_buckets(string id, string tag, string expected)
    {
        var entry = new ItemCatalogEntry(id, id, null, null, 1, 0, false, 0, [tag]);

        Assert.Equal(expected, ItemCatalogService.CategoryOf(entry));
    }

    [Fact]
    public async Task Unknown_items_do_not_attempt_to_serve_an_icon()
    {
        using var catalog = new ItemCatalogService();

        Assert.Null(await catalog.GetIconPathAsync("missing-test-item"));
    }
}
