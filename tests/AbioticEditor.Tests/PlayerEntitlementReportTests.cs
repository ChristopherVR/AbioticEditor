using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

/// <summary>
/// Tests for the read-only per-player entitlement report. The fixtures carry only ownership
/// tokens (no recipe tokens and no top-level <c>UserEntitlements</c> map), so the recipe-resolution
/// paths are exercised by adding a recipe-shaped token through the existing in-memory entitlement
/// editor and reading it back.
/// </summary>
public sealed class PlayerEntitlementReportTests
{
    private static readonly RecipeInfo Bandage = new("recipe_bandage", "Bandage", 1, "Tools", "Crafting");

    [Fact]
    public void Fixture_metadata_saves_hold_only_ownership_tokens_grouped_per_player()
    {
        if (!AllFixtureSaves.MetadataSaves.Any()) return;

        var sawTwoPlayers = false;
        foreach (var (_, meta) in AllFixtureSaves.MetadataSaves)
        {
            var report = PlayerEntitlementReport.Build(meta);
            Assert.All(report, p =>
            {
                Assert.Equal("ServerEntitlements", p.Source);
                Assert.NotEmpty(p.Tokens);
                Assert.All(p.Tokens, t => Assert.Equal(EntitlementTokenKind.Ownership, t.Kind));
                Assert.Empty(p.RecipeTokens);
            });
            Assert.Equal(report.Count, report.Select(p => p.SteamId).Distinct().Count());
            sawTwoPlayers |= report.Count >= 2;
        }
        Assert.True(sawTwoPlayers, "the dedicated-server fixture has two entitled players");
    }

    [Fact]
    public void Region_saves_have_no_entitlements()
    {
        var region = AllFixtureSaves.RegionSaves.FirstOrDefault();
        if (region.Data is null) return;
        Assert.Empty(PlayerEntitlementReport.Build(region.Data));
    }

    [Fact]
    public void Persona_names_are_attached_when_a_lookup_is_supplied()
    {
        var meta = AllFixtureSaves.MetadataSaves.FirstOrDefault(m => PlayerEntitlementReport.Build(m.Data).Count > 0);
        if (meta.Data is null) return;

        var report = PlayerEntitlementReport.Build(meta.Data, personaFor: id => "name-" + id);
        Assert.All(report, p => Assert.Equal("name-" + p.SteamId, p.PersonaName));
    }

    [Fact]
    public void Classify_resolves_recipes_through_the_catalog_and_degrades_without_one()
    {
        var byId = new Dictionary<string, RecipeInfo>(StringComparer.OrdinalIgnoreCase) { [Bandage.Id] = Bandage };

        var resolved = PlayerEntitlementReport.Classify("recipe_bandage", byId, id => id + "!", new HashSet<string> { "recipe_bandage" });
        Assert.Equal(EntitlementTokenKind.Recipe, resolved.Kind);
        Assert.True(resolved.ResolvedInCatalog);
        Assert.Equal("Bandage!", resolved.Label);
        Assert.True(resolved.InWorldRecipeUnlocks);

        // No catalog (game not installed): still recognised by shape, shown raw, no world comparison guess.
        var raw = PlayerEntitlementReport.Classify("srecipe_soup", null, null, null);
        Assert.Equal(EntitlementTokenKind.Recipe, raw.Kind);
        Assert.False(raw.ResolvedInCatalog);
        Assert.Null(raw.Label);
        Assert.Null(raw.InWorldRecipeUnlocks);

        Assert.Equal(EntitlementTokenKind.Unknown, PlayerEntitlementReport.Classify("SomethingNew", null, null, null).Kind);
        Assert.Equal(EntitlementTokenKind.Ownership, PlayerEntitlementReport.Classify("earlyaccess", null, null, null).Kind);
    }

    [Fact]
    public void A_recipe_token_added_to_a_player_is_reported_and_shown_read_only_in_the_feature()
    {
        var meta = AllFixtureSaves.MetadataSaves.FirstOrDefault(m => PlayerEntitlementReport.Build(m.Data).Count > 0);
        if (meta.Data is null) return;

        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entry = feature.Read(meta.Data.Raw)[0];
        Assert.DoesNotContain(entry.Fields, f => f.Id == "recipeEntitlements");

        Assert.True(feature.SetField(meta.Data.Raw, entry.Key, "addEntitlement", "recipe_bandage").Changed);

        var report = PlayerEntitlementReport.Build(meta.Data, [Bandage], id => "Bandage");
        var player = report.Single(p => p.SteamId == entry.Key);
        var recipe = Assert.Single(player.RecipeTokens);
        Assert.Equal("recipe_bandage", recipe.Token);
        Assert.Equal("Bandage", recipe.Label);
        Assert.True(recipe.ResolvedInCatalog);

        var summary = feature.Read(meta.Data.Raw).Single(e => e.Key == entry.Key).Fields.Single(f => f.Id == "recipeEntitlements");
        Assert.False(summary.Editable);
        Assert.Contains("recipe_bandage", summary.Value);
    }
}
