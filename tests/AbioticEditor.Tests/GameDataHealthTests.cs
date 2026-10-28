using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

public sealed class GameDataHealthTests
{
    [Theory]
    [InlineData(1624, GameDataHealthStatus.Healthy)]
    [InlineData(GameDataHealth.MinimumPlausibleItemRows, GameDataHealthStatus.Healthy)]
    [InlineData(GameDataHealth.MinimumPlausibleItemRows - 1, GameDataHealthStatus.MappingsOutdated)]
    [InlineData(0, GameDataHealthStatus.MappingsOutdated)]
    public void An_item_table_that_reads_back_nearly_empty_means_the_mappings_are_outdated(int rows, GameDataHealthStatus expected)
        => Assert.Equal(expected, GameDataHealth.Classify(rows));

    [Fact]
    public void No_answer_is_unreadable_not_outdated()
        => Assert.Equal(GameDataHealthStatus.Unreadable, GameDataHealth.Classify(null));

    [SkippableFact]
    public void The_installed_game_reads_back_healthy_with_the_bundled_mappings()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        Skip.If(provider is null || !provider.HasMappings, "the game is not installed on this machine");

        Assert.Equal(GameDataHealthStatus.Healthy, GameDataHealth.Check(provider!));
    }

    /// <summary>
    /// Opt-in: set ABIOTIC_STALE_USMAP to a mappings file from an older game build. Against a newer
    /// installed game it must be reported as outdated, which is the state the notice exists for.
    /// </summary>
    [SkippableFact]
    public void Mappings_from_an_older_game_build_are_reported_as_outdated()
    {
        var stale = Environment.GetEnvironmentVariable("ABIOTIC_STALE_USMAP");
        Skip.If(string.IsNullOrEmpty(stale) || !File.Exists(stale), "no stale mappings file supplied");
        using var provider = GameAssetProvider.CreateForLocalInstall(mappingsPath: stale, includeMods: false);
        Skip.If(provider is null, "the game is not installed on this machine");

        Assert.Equal(GameDataHealthStatus.MappingsOutdated, GameDataHealth.Check(provider!));
    }
}
