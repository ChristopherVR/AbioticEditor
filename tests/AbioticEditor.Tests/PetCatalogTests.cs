using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>The pet family/variant catalog and the XP -> level curve.</summary>
public class PetCatalogTests
{
    [Fact]
    public void Curated_table_is_well_formed_and_covers_every_family()
    {
        var curated = PetCatalog.Curated;
        Assert.NotEmpty(curated);

        // No duplicate short-names; every class path is the canonical Package.Asset_C shape.
        Assert.Equal(curated.Count, curated.Select(v => v.ShortClass).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(curated, v => Assert.EndsWith($"{v.ShortClass}_C", v.ClassPath, StringComparison.Ordinal));

        // All four families present; summons are flagged non-editable.
        foreach (var cat in new[] { PetCategory.Pest, PetCategory.Peccary, PetCategory.Skink, PetCategory.Other })
        {
            Assert.Contains(curated, v => v.Category == cat);
        }
        Assert.All(curated.Where(v => v.IsSummon), v => Assert.False(v.IsEditable));
    }

    [Theory]
    [InlineData("/Game/Blueprints/Characters/NPCs/NPC_Monster_Pest_Electro.NPC_Monster_Pest_Electro_C", PetCategory.Pest)]
    [InlineData("NPC_Peccary_Sow", PetCategory.Peccary)]
    [InlineData("NPC_Peccary_Tareccary", PetCategory.Peccary)]
    [InlineData("NPC_Skink_Magma", PetCategory.Skink)]
    [InlineData("NPC_Exor", PetCategory.Other)]
    public void Categorize_matches_known_families(string cls, PetCategory expected)
        => Assert.Equal(expected, PetCatalog.Categorize(cls));

    [Fact]
    public void Unknown_pest_variant_is_classified_by_heuristic()
    {
        // A future pest the curated table has never seen still buckets into Pests.
        const string future = "NPC_Monster_Pest_FromAFuturePatch";
        Assert.Equal(PetCategory.Pest, PetCatalog.Categorize(future));
        Assert.True(PetCatalog.IsPetClass(future));
        Assert.Equal("Pest FromAFuturePatch", PetCatalog.FriendlyName(future));
    }

    [Fact]
    public void Non_pet_npc_is_not_listed_as_a_pet()
    {
        Assert.False(PetCatalog.IsPetClass("NPC_NarrativeNPC_Human_Hologram"));
    }

    [Fact]
    public void Summons_are_detected()
    {
        Assert.True(PetCatalog.IsSummon("NPC_Exor"));
        Assert.True(PetCatalog.IsSummon("NPC_Mystagogue"));
        Assert.False(PetCatalog.IsSummon("NPC_Peccary_Sow"));
    }

    [Fact]
    public void Xp_and_level_are_consistent()
    {
        Assert.Equal(0, PetCatalog.LevelForXp(0));
        Assert.Equal(0, PetCatalog.XpForLevel(0));
        Assert.Equal(PetCatalog.MaxLevel, PetCatalog.LevelForXp(100_000));
        Assert.Equal(750, PetCatalog.XpForLevel(PetCatalog.MaxLevel));

        // Round-trip: the XP for a level lands back on that level, and levels are monotonic.
        var last = -1;
        for (var lvl = 0; lvl <= PetCatalog.MaxLevel; lvl++)
        {
            var xp = PetCatalog.XpForLevel(lvl);
            Assert.True(xp > last, $"XP threshold must increase at level {lvl}");
            last = xp;
            Assert.Equal(lvl, PetCatalog.LevelForXp(xp));
        }
    }

    [Fact]
    public void BuildVariants_without_paks_falls_back_to_curated()
    {
        var variants = PetCatalog.BuildVariants(null);
        Assert.Equal(PetCatalog.Curated.Count, variants.Count);
        // Ordered by family then name.
        Assert.Equal(variants.OrderBy(v => v.Category).ThenBy(v => v.FriendlyName, StringComparer.OrdinalIgnoreCase),
            variants);
    }
}
