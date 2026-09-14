using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

public sealed class ItemVariantCatalogTests
{
    private static ItemVariantCatalog Catalog() => ItemVariantCatalog.FromRegistry(
    [
        new("poster_0091", "IS-0091 Notice", "A notice poster.", "/Game/poster_0091"),
        new("poster_Detour", "\"DETOUR\" Poster", null, null),
        new("hydrohat_yellow", "Yellow Hydroplant Hat", null, null),
        new("antiqueshotgun_polished", "Polished Shotgun", null, null),
    ]);

    [Fact]
    public void Find_IsCaseInsensitive_AndPreservesMetadata()
    {
        var entry = Catalog().Find("POSTER_0091");

        Assert.NotNull(entry);
        Assert.Equal("IS-0091 Notice", entry!.DisplayName);
        Assert.Equal("A notice poster.", entry.Description);
        Assert.Equal("/Game/poster_0091", entry.IconAssetPath);
    }

    [Fact]
    public void ForItem_ReturnsReviewedRows_WithRegistryNamesAndFallbacks()
    {
        var variants = Catalog().ForItem("poster");

        Assert.Equal(8, variants.Count);
        Assert.Equal("IS-0091 Notice", variants[0].DisplayName);
        Assert.Contains(variants, variant => variant.RowName == "poster_Detour"
                                            && variant.DisplayName == "\"DETOUR\" Poster");
        Assert.Contains(variants, variant => variant.RowName == "poster_VOTV_04"
                                            && variant.DisplayName == "Poster VOTV 04");
    }

    [Fact]
    public void ForItem_SeparatesFixedUpgradeAppearancesFromReviewedChoices()
    {
        var catalog = Catalog();

        Assert.Empty(catalog.ForItem("shotgun_doublebarrel_U1"));
        Assert.NotNull(catalog.Find("antiqueshotgun_polished"));
    }

    [Fact]
    public void ForItem_RecognizesItemIdsCaseInsensitively()
        => Assert.Contains(Catalog().ForItem("ARMOR_HELMET_HYDROHAT"),
            variant => variant.RowName == "hydrohat_yellow");
}
