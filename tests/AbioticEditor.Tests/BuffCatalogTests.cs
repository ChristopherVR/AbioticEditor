using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

public class BuffCatalogTests
{
    [Fact]
    public void Empty_catalog_finds_nothing()
    {
        Assert.Equal(0, BuffCatalog.Empty.Count);
        Assert.Null(BuffCatalog.Empty.Find("Debuff_Stinky"));
        Assert.Null(BuffCatalog.Empty.Find(null));
    }

    [Fact]
    public void Installed_game_table_describes_the_saved_effects()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return; // no install: nothing to check
        var catalog = BuffCatalog.LoadFrom(provider);
        var stinky = catalog.Find("Debuff_Stinky");
        Assert.NotNull(stinky);
        Assert.Equal("Stinky", stinky!.DisplayName);
        Assert.True(stinky.IsSaved);
        Assert.True(stinky.NoExpiration); // matches the -1 expiry in the fixture save
        Assert.True(catalog.Count > 100);
    }
}
