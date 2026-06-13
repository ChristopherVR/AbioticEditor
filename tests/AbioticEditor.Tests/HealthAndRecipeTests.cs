using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class HealthAndRecipeTests
{
    private readonly ITestOutputHelper _output;

    public HealthAndRecipeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_LimbHealth()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        // Fixture values verified in the schema dump.
        Assert.Equal(90, data.Health.Head);
        Assert.Equal(100, data.Health.Torso);
        Assert.Equal(100, data.Health.RightLeg);
    }

    [Fact]
    public void ApplyLimbHealth_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        PlayerSaveWriter.ApplyLimbHealth(data, new LimbHealth(75, 80, 85, 90, 95, 100));

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(75, reloaded.Health.Head);
        Assert.Equal(80, reloaded.Health.Torso);
        Assert.Equal(85, reloaded.Health.LeftArm);
        Assert.Equal(100, reloaded.Health.RightLeg);
    }

    [Fact]
    public void Reads_PlayerRecipes()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        Assert.True(data.Recipes.Count > 300);
        Assert.Contains("recipe_bandage", data.Recipes);
    }

    [Fact]
    public void ApplyRecipes_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var updated = data.Recipes.Append("recipe_test_sentinel").ToList();
        PlayerSaveWriter.ApplyRecipes(data, updated);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(updated.Count, reloaded.Recipes.Count);
        Assert.Contains("recipe_test_sentinel", reloaded.Recipes);
    }

    [Fact]
    public void Reads_GlobalRecipes_FromMetadata()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        if (!File.Exists(path)) return;

        var data = WorldSaveReader.ReadFromFile(path);
        Assert.True(data.GlobalRecipes.Count > 300);
        Assert.Contains("recipe_bandage", data.GlobalRecipes);
    }

    [Fact]
    public void ApplyGlobalRecipes_RoundTripsThroughSerializer()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        if (!File.Exists(path)) return;

        var data = WorldSaveReader.ReadFromFile(path);
        var updated = data.GlobalRecipes.Append("recipe_test_sentinel").ToList();
        WorldSaveWriter.ApplyGlobalRecipes(data, updated);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        Assert.Equal(updated.Count, reloaded.GlobalRecipes.Count);
        Assert.Contains("recipe_test_sentinel", reloaded.GlobalRecipes);
    }

    [Fact]
    public void RecipeCatalog_CoversSaveRecipes()
    {
        var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings)
        {
            _output.WriteLine("No install/mappings — skipping.");
            return;
        }

        using (provider)
        {
            var all = RecipeCatalog.LoadFrom(provider);
            _output.WriteLine($"Recipe catalog: {all.Count} rows");
            Assert.True(all.Count >= 480);

            var path = FixturePlayerSave();
            if (path is null) return;
            var data = PlayerSaveReader.ReadFromFile(path);
            var unknown = data.Recipes.Where(r => !all.Contains(r, StringComparer.Ordinal)).ToList();
            _output.WriteLine($"Save recipes not in catalog: {unknown.Count}");
            foreach (var u in unknown.Take(20)) _output.WriteLine("  " + u);
            // Old saves may carry renamed/removed rows, but the bulk must be covered.
            Assert.True(unknown.Count < data.Recipes.Count / 10);
        }
    }
}
