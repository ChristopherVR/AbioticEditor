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
        new("tv_tips_designations", "TV: Immurement and You", null, null),
        new("tv_tips_wayseeker", "TV: ???", null, null),
        new("photoframe_acahn", "Remembrance Photo Frame", "A photo frame of an inventor.", null),
        new("photoframe_votv_01", "Sol", null, null),
        new("painting_coldmountains", "Cold Mountains", null, null),
        new("painting_L_orb", "Marble", null, null),
        new("painting_S_plague", "Plague", null, null),
        new("painting_V_jack", "Creepy Pumpkin", null, null),
        new("fridge_office_gray", "Gray Office Fridge", null, null),
        new("office_couch_grey", "Grey Office Couch", null, null),
        new("armchair_IS0018", "IS-0018 Armchair", null, null),
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

    [Fact]
    public void ForItem_TelevisionOffersTipsAndStandbyScreens()
    {
        var variants = Catalog().ForItem("TV");

        Assert.Contains(variants, v => v.RowName == "tv_tips_designations");
        Assert.Contains(variants, v => v.RowName == "tv_tips_wayseeker");
    }

    [Fact]
    public void ForItem_DeskPaintingOffersPhotoFrameArtwork()
    {
        var variants = Catalog().ForItem("Painting_Desk");

        Assert.Contains(variants, v => v.RowName == "photoframe_acahn" && v.DisplayName == "Remembrance Photo Frame");
        Assert.Contains(variants, v => v.RowName == "photoframe_votv_01");
    }

    [Fact]
    public void ForItem_PaintingFramesOfferTheirPrefixedArtworkGroup()
    {
        var catalog = Catalog();

        Assert.Contains(catalog.ForItem("Painting_Landscape"), v => v.RowName == "painting_coldmountains");
        Assert.Contains(catalog.ForItem("Painting_Landscape_Fancy"), v => v.RowName == "painting_coldmountains");
        Assert.Contains(catalog.ForItem("Painting_Landscape_Large"), v => v.RowName == "painting_L_orb");
        Assert.Contains(catalog.ForItem("Painting_Square"), v => v.RowName == "painting_S_plague");
        Assert.Contains(catalog.ForItem("Painting_Vertical"), v => v.RowName == "painting_V_jack");
    }

    [Fact]
    public void ForItem_LandscapePaintingsIncludeTheWallArtSeriesFoundOnRealPlacedInstances()
    {
        // painting_a_2..12 (M_WallArt_* materials) were unmapped until a real Cascade save
        // showed TextureVariantRow="painting_a_N" on Deployed_Painting_Landscape/_Fancy and
        // Deployed_Painting_Square_Fancy actors - see ItemVariantCatalog's CuratedRows comment.
        var catalog = Catalog();

        Assert.Contains(catalog.ForItem("Painting_Landscape"), v => v.RowName == "painting_a_2");
        Assert.Contains(catalog.ForItem("Painting_Landscape"), v => v.RowName == "painting_a_12");
        Assert.Contains(catalog.ForItem("Painting_Landscape_Fancy"), v => v.RowName == "painting_a_7");
        Assert.Contains(catalog.ForItem("Painting_Landscape_Fancy"), v => v.RowName == "painting_a_11");
        Assert.Contains(catalog.ForItem("Painting_Square_Fancy"), v => v.RowName == "painting_a_3");

        // painting_a_4 and painting_a_5 were searched for the same way and never found in any
        // available save or backup; they stay unmapped.
        Assert.DoesNotContain(catalog.ForItem("Painting_Landscape"), v => v.RowName == "painting_a_4");
        Assert.DoesNotContain(catalog.ForItem("Painting_Landscape"), v => v.RowName == "painting_a_5");
    }

    [Fact]
    public void ForItem_OfficeFurnitureFamiliesShareTheirRows()
    {
        var catalog = Catalog();

        Assert.Contains(catalog.ForItem("Deployable_Fridge"), v => v.RowName == "fridge_office_gray");
        Assert.Contains(catalog.ForItem("Deployable_Couch_Office_Medium_01"), v => v.RowName == "office_couch_grey");
        Assert.Contains(catalog.ForItem("Deployable_Couch_Office_Long_01"), v => v.RowName == "office_couch_grey");
    }

    [Fact]
    public void ForItem_DoesNotOfferAnotherItemsFixedDefaultAppearance()
    {
        // armchair_IS0018 is the fixed appearance of the standalone IS0018 item, not a
        // selectable skin for the Fancy Armchair family; it must not leak into any picker.
        var catalog = Catalog();

        Assert.DoesNotContain(catalog.ForItem("Armchair_Fancy"), v => v.RowName == "armchair_IS0018");
        Assert.DoesNotContain(catalog.ForItem("Armchair_Tall_Fancy"), v => v.RowName == "armchair_IS0018");
    }

    [Fact]
    public void Find_PreservesAnUnrecognizedSavedRow()
    {
        // hydrohat_blue was observed in a real save's ChangeableData but is not a current
        // DT_TextureVariants row (renamed/removed upstream); the catalog must not throw and
        // must simply report it as unknown rather than inventing data for it.
        var catalog = Catalog();

        Assert.Null(catalog.Find("hydrohat_blue"));
    }
}
