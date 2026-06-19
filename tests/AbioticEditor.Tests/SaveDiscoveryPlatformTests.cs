using AbioticEditor.Core.Saves;

namespace AbioticEditor.Tests;

/// <summary>
/// The platform tag on discovered worlds: a Steam-id account folder is STEAM, any other client
/// account is UNKNOWN, and an Xbox container world is GAME PASS.
/// </summary>
public class SaveDiscoveryPlatformTests
{
    [Fact]
    public void Steam_account_folder_is_tagged_steam()
    {
        if (Fixtures.ClientSavedDir is null) return; // fixture absent - skip
        var worlds = SaveDiscovery.DiscoverClientWorlds(Fixtures.ClientSavedDir!);
        Assert.NotEmpty(worlds);
        Assert.All(worlds, w =>
        {
            Assert.Equal(SavePlatform.Steam, w.Platform);
            Assert.Equal("STEAM", w.PlatformLabel);
            Assert.False(w.IsGamePassContainer);
        });
    }

    [Fact]
    public void Non_steam_account_folder_is_tagged_unknown()
    {
        var root = Directory.CreateTempSubdirectory("discovery-unknown");
        try
        {
            // A client SaveGames tree whose account folder is not a SteamID64.
            var world = Path.Combine(root.FullName, "epic-9f8e7d", "Worlds", "MyWorld");
            Directory.CreateDirectory(world);
            File.WriteAllBytes(Path.Combine(world, "WorldSave_MetaData.sav"), new byte[] { 0x47, 0x56, 0x41, 0x53 });

            var worlds = SaveDiscovery.DiscoverClientWorlds(root.FullName);
            var w = Assert.Single(worlds);
            Assert.Equal(SavePlatform.Unknown, w.Platform);
            Assert.Equal("UNKNOWN", w.PlatformLabel);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void GamePass_container_world_is_tagged_game_pass()
    {
        if (Fixtures.GamePassWgsDir is null) return; // fixture absent - skip

        var store = AbioticEditor.Core.GamePass.WgsContainerStore.Open(Fixtures.GamePassWgsDir!);
        var wc = store.Containers.First(c => c.Name.EndsWith("-WC", StringComparison.OrdinalIgnoreCase));

        // Mirror what DiscoverGamePassContainerWorlds builds for a wgs world.
        var world = new DiscoveredWorld(Fixtures.GamePassWgsDir!, wc.Name[..^3], DiscoveredWorldSource.Client, "acct")
        {
            Platform = SavePlatform.GamePass,
            GamePassContainer = wc.Name,
        };
        Assert.Equal("GAME PASS", world.PlatformLabel);
        Assert.True(world.IsGamePassContainer);
    }
}
