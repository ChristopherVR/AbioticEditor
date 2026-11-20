using System.Text.Json;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

public sealed class ModIoInstallLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-modio-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Completed_Abiotic_installs_are_available_wherever_the_index_points()
    {
        var game = Path.Combine(_root, "13105");
        var installed = Path.Combine(game, "mods", "42");
        var unrelated = Path.Combine(_root, "other-game");
        Directory.CreateDirectory(installed);
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(installed, "content.pak"), "pak");
        File.WriteAllText(Path.Combine(installed, "content.utoc"), "utoc");
        File.WriteAllText(Path.Combine(installed, "content.ucas"), "ucas");
        File.WriteAllText(Path.Combine(installed, "incomplete.utoc"), "utoc");
        File.WriteAllText(Path.Combine(unrelated, "outside.pak"), "pak");
        WriteState(new object[]
        {
            new { ID = 999, State = "bad", PathOnDisk = installed },
            new { ID = 42, State = 1, PathOnDisk = installed, Profile = new { game_id = 13105, name = "My companions" } },
            new { ID = 43, State = 3, PathOnDisk = installed },
            new { ID = 44, State = 1, PathOnDisk = unrelated },
            new { ID = 45, State = 1, PathOnDisk = installed, Profile = new { game_id = 123, name = "Other game" } },
            new { ID = 46, State = 1, PathOnDisk = "relative/path" },
        });
        var mods = ModIoInstallLocator.FindInstalledAt(_root);
        Assert.Equal(["mod.io/42", "mod.io/44"], mods.Select(m => m.Name).Order(StringComparer.Ordinal));
        var mod = mods.Single(m => m.ModIoId == 42);
        Assert.Equal("mod.io/42", mod.Name);
        Assert.Equal("My companions", mod.DisplayName);
        Assert.Equal(42, mod.ModIoId);
        Assert.Equal(2, mod.Files.Count);
        Assert.Contains(mod.Files, f => f.EndsWith("content.pak", StringComparison.Ordinal));
        Assert.Contains(mod.Files, f => f.EndsWith("content.utoc", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_or_invalid_cache_does_not_break_base_game_discovery()
    {
        Assert.Empty(ModIoInstallLocator.FindInstalledAt(_root));
        WriteState([]);
        File.WriteAllText(Path.Combine(_root, "13105", "metadata", "state.json"), "invalid");
        Assert.Empty(ModIoInstallLocator.FindInstalledAt(_root));
    }

    [Fact]
    public void Storage_folder_follows_user_then_global_then_default()
    {
        var settings = Path.Combine(_root, "settings");
        var fallback = Path.Combine(_root, "public");
        Directory.CreateDirectory(settings);
        Assert.Equal([fallback], ModIoInstallLocator.StorageRootsFor(settings, fallback));

        var global = Path.Combine(_root, "global-root");
        File.WriteAllText(Path.Combine(settings, "globalsettings.json"), JsonSerializer.Serialize(new { RootLocalStoragePath = global }));
        Assert.Equal([global], ModIoInstallLocator.StorageRootsFor(settings, fallback));

        var user = Path.Combine(settings, "13105", "player");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "user.json"), "{\"subscriptions\":[1]}");
        Assert.Equal([global], ModIoInstallLocator.StorageRootsFor(settings, fallback));

        var perGame = Path.Combine(_root, "per-game-root");
        File.WriteAllText(Path.Combine(user, "user.json"), JsonSerializer.Serialize(new { RootLocalStoragePath = perGame }));
        Assert.Equal([perGame], ModIoInstallLocator.StorageRootsFor(settings, fallback));
    }

    private void WriteState(object[] mods)
    {
        var metadata = Path.Combine(_root, "13105", "metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "state.json"), JsonSerializer.Serialize(new { Mods = mods }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
