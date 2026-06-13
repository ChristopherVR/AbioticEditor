using AbioticEditor.Core.Saves;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="SaveDiscovery"/> against the two real fixture layouts: the client
/// tree (steamid\Worlds\World) and the dedicated-server tree (SaveGames\Server\Worlds).
/// </summary>
public class SaveDiscoveryTests
{
    [Fact]
    public void Discovers_client_worlds_from_fixture_layout()
    {
        Assert.NotNull(Fixtures.ClientSavedDir);

        var worlds = SaveDiscovery.DiscoverClientWorlds(Fixtures.ClientSavedDir!);

        Assert.NotEmpty(worlds);
        Assert.All(worlds, w => Assert.Equal(DiscoveredWorldSource.Client, w.Source));
        Assert.All(worlds, w => Assert.False(string.IsNullOrEmpty(w.AccountId)));
        Assert.Contains(worlds, w => w.WorldName == "Cascade");
        Assert.All(worlds, w => Assert.True(w.SaveFileCount > 0));
        Assert.All(worlds, w => Assert.True(Directory.Exists(w.FolderPath)));
    }

    [Fact]
    public void Discovers_server_worlds_under_arbitrary_root()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        // fixtures/Server/Worlds/Cascade -> scan from fixtures/ (two levels above Worlds).
        var serverRoot = Path.GetDirectoryName(Path.GetDirectoryName(Fixtures.ServerWorldsDir!))!;

        var worlds = SaveDiscovery.DiscoverUnderRoot(serverRoot, DiscoveredWorldSource.DedicatedServer);

        var cascade = Assert.Single(worlds, w => w.WorldName == "Cascade");
        Assert.Equal("SERVER", cascade.SourceLabel);
        Assert.Null(cascade.AccountId);
        Assert.True(cascade.SaveFileCount > 10, $"expected the full world, got {cascade.SaveFileCount} files");
    }

    [Fact]
    public void Backups_are_not_reported_as_worlds()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var serverRoot = Path.GetDirectoryName(Path.GetDirectoryName(Fixtures.ServerWorldsDir!))!;

        var worlds = SaveDiscovery.DiscoverUnderRoot(serverRoot, DiscoveredWorldSource.DedicatedServer);

        // The fixture has Backups/Cascade/1..5; only the live world must surface.
        Assert.All(worlds, w => Assert.DoesNotContain("Backups", w.FolderPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_roots_return_empty()
    {
        Assert.Empty(SaveDiscovery.DiscoverClientWorlds(Path.Combine(Path.GetTempPath(), "no-such-dir-409")));
        Assert.Empty(SaveDiscovery.DiscoverServerWorlds(Path.Combine(Path.GetTempPath(), "no-such-dir-409")));
        Assert.Empty(SaveDiscovery.DiscoverUnderRoot(Path.Combine(Path.GetTempPath(), "no-such-dir-409"), DiscoveredWorldSource.Client));
    }

    [Fact]
    public void DiscoverAll_does_not_throw_on_this_machine()
    {
        // Machine-dependent content; the contract is just "never throws, returns a list".
        var worlds = SaveDiscovery.DiscoverAll();
        Assert.NotNull(worlds);
    }
}
