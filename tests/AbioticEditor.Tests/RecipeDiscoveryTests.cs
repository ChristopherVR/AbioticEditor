using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class RecipeDiscoveryTests
{
    private readonly ITestOutputHelper _output;

    public RecipeDiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    // A future patch adding a new recipe table must be picked up.
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_BakingRecipes.uasset", true)]
    [InlineData("abioticfactor/content/blueprints/datatables/dt_brewingrecipes.uasset", true)]
    // The three known tables are loaded by name - not "discovered".
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_Recipes.uasset", false)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_SoupRecipes.uasset", false)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_ChemistryRecipes.uasset", false)]
    // Wrong folder, wrong name shape, or wrong extension.
    [InlineData("AbioticFactor/Content/Blueprints/DT_FooRecipes.uasset", false)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_Emails.uasset", false)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/Recipes.uasset", false)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/DT_FooRecipes.uexp", false)]
    public void IsDiscoveredRecipeTable_MatchesExpectedPaths(string assetPath, bool expected)
    {
        Assert.Equal(expected, RecipeCatalog.IsDiscoveredRecipeTable(assetPath));
    }

    [Fact]
    public void LoadInfos_KnownTablesStillLoad_AndDiscoveryRuns()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings)
        {
            _output.WriteLine("No install/mappings — skipping.");
            return;
        }

        // The discovery pass must not crash and (with current game data) finds nothing
        // beyond the three known tables.
        var discovered = RecipeCatalog.DiscoverExtraTables(provider);
        _output.WriteLine($"discovered extra recipe tables: {discovered.Count} " +
                          $"[{string.Join(", ", discovered)}]");

        var infos = RecipeCatalog.LoadInfosFrom(provider);
        _output.WriteLine($"total recipe rows: {infos.Count}");

        // All three known tables still contribute rows.
        Assert.Contains(infos, r => r.Source == "Crafting");
        Assert.Contains(infos, r => r.Source == "Soup");
        Assert.Contains(infos, r => r.Source == "Chemistry");
        Assert.True(infos.Count > 300, $"Expected the usual full vocabulary, got {infos.Count}.");

        // Misc rows can only come from discovered tables.
        var miscRows = infos.Count(r => r.Source == RecipeCatalog.DiscoveredSource);
        _output.WriteLine($"Misc rows: {miscRows}");
        if (discovered.Count == 0)
        {
            Assert.Equal(0, miscRows);
        }
    }
}
