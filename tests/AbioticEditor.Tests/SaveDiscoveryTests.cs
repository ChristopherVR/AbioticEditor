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
        Assert.Empty(SaveDiscovery.DiscoverPackagedClientWorlds(Path.Combine(Path.GetTempPath(), "no-such-dir-409")));
    }

    [Fact]
    public void Discovers_GamePass_world_under_packaged_redirect_with_nonnumeric_account()
    {
        using var tmp = new TempDir();
        // %LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\AbioticFactor\Saved\SaveGames\<account>\Worlds\<World>
        var account = "MSFT_a1b2c3"; // non-numeric Game Pass-style owner id
        var worldDir = Path.Combine(
            tmp.Path, "PlayStack.AbioticFactor_1a2b3c4d5e6f", "LocalCache", "Local",
            "AbioticFactor", "Saved", "SaveGames", account, "Worlds", "Cascade");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "WorldSave_Cascade.sav"), "stub");

        var worlds = SaveDiscovery.DiscoverPackagedClientWorlds(tmp.Path);

        var cascade = Assert.Single(worlds, w => w.WorldName == "Cascade");
        Assert.Equal(DiscoveredWorldSource.Client, cascade.Source);
        Assert.Equal(account, cascade.AccountId); // opaque, non-numeric id preserved
        Assert.True(cascade.SaveFileCount > 0);
    }

    [Fact]
    public void Packaged_discovery_finds_moved_SaveGames_only_in_an_Abiotic_package()
    {
        using var tmp = new TempDir();
        // A non-standard subpath that the known-subpath probe won't hit; only the deeper
        // fallback (gated to Abiotic-named packages) should find it.
        var worldDir = Path.Combine(
            tmp.Path, "Vendor.AbioticFactor_xyz", "LocalState", "Custom", "Nested",
            "AbioticFactor", "Saved", "SaveGames", "76561190000000001", "Worlds", "Reactor");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "WorldSave_Reactor.sav"), "stub");

        // An unrelated package with the same nested layout must be ignored (no deep scan there).
        var otherWorld = Path.Combine(
            tmp.Path, "Unrelated.OtherApp_abc", "LocalState", "Custom", "Nested",
            "AbioticFactor", "Saved", "SaveGames", "acct", "Worlds", "Ghost");
        Directory.CreateDirectory(otherWorld);
        File.WriteAllText(Path.Combine(otherWorld, "WorldSave_Ghost.sav"), "stub");

        var worlds = SaveDiscovery.DiscoverPackagedClientWorlds(tmp.Path);

        Assert.Contains(worlds, w => w.WorldName == "Reactor");
        Assert.DoesNotContain(worlds, w => w.WorldName == "Ghost");
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Path);

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-disco-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void DiscoverAll_does_not_throw_on_this_machine()
    {
        // Machine-dependent content; the contract is just "never throws, returns a list".
        var worlds = SaveDiscovery.DiscoverAll();
        Assert.NotNull(worlds);
    }
}
