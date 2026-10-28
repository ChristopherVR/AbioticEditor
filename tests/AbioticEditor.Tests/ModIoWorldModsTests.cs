using System.Text.Json;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// mod.io support beyond finding installs: SDK states other than "installed", caches without an
/// index, the game's own enabled list, a world's mod list and how it compares with what is loaded,
/// plus an opt-in check against real downloaded mods (set <c>ABIOTIC_MODIO_ROOT</c>).
/// </summary>
public sealed class ModIoWorldModsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-modio-world-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public ModIoWorldModsTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Update_pending_mods_still_load_and_unfinished_ones_are_reported_as_downloading()
    {
        var updating = Install(10, "Updating.pak");
        var downloading = Path.Combine(_root, "13105", "mods", "11");
        WriteState(
            new { ID = 10, State = 2, PathOnDisk = updating, Profile = new { game_id = 13105, name = "Being updated" } },
            new { ID = 11, State = 3, PathOnDisk = downloading, Profile = new { game_id = 13105, name = "Half way" } },
            new { ID = 12, State = 0, Profile = new { game_id = 13105, name = "Queued" } },
            new { ID = 13, State = 5, PathOnDisk = updating, Profile = new { game_id = 13105, name = "Unsubscribed" } });

        var installed = Assert.Single(ModIoInstallLocator.FindInstalledAt(_root));
        Assert.Equal("Being updated", installed.DisplayName);

        var pending = ModIoInstallLocator.FindPendingAt(_root, [10, 14]);
        Assert.Equal(["Half way", "mod.io #14", "Queued"], pending.Select(mod => mod.DisplayName));
    }

    [Fact]
    public void A_cache_without_an_index_uses_the_extracted_mod_folders_and_plugin_names()
    {
        var folder = Install(6368513, Path.Combine("Content", "Paks", "Windows", "LinkedAbioticFactor-Windows.pak"));
        File.WriteAllText(Path.Combine(folder, "Linked.uplugin"), """{ "FriendlyName": "LinkedBenchPower", "CanContainContent": true }""");
        Directory.CreateDirectory(Path.Combine(_root, "13105", "mods", "not-a-mod-id"));

        var mod = Assert.Single(ModIoInstallLocator.FindInstalledAt(_root));
        Assert.Equal(ModIoInstallLocator.KeyFor(6368513), mod.Name);
        Assert.Equal("LinkedBenchPower", mod.DisplayName);
        Assert.Equal(6368513, mod.ModIoId);
    }

    [Fact]
    public void An_index_entry_without_a_profile_name_uses_the_plugin_name()
    {
        var folder = Install(42, "Mod.pak");
        File.WriteAllText(Path.Combine(folder, "Mod.uplugin"), """{ "FriendlyName": "Robocart" }""");
        WriteState(new { ID = 42, State = 1, PathOnDisk = folder + Path.DirectorySeparatorChar });

        Assert.Equal("Robocart", Assert.Single(ModIoInstallLocator.FindInstalledAt(_root)).DisplayName);
    }

    [Theory]
    [InlineData("EnabledMods=6368513\nEnabledMods=6486047\n", new long[] { 6368513, 6486047 })]
    [InlineData("+EnabledMods=6368513\n", new long[] { 6368513 })]
    [InlineData("EnabledMods=(6368513,6486047)\n", new long[] { 6368513, 6486047 })]
    [InlineData("AllowModsSetting=Initial\nModDependencyCache=()\n", new long[0])]
    public void The_game_enabled_mod_list_is_read_from_its_user_settings(string lines, long[] expected)
    {
        Directory.CreateDirectory(_root);
        var ini = Path.Combine(_root, "GameUserSettings.ini");
        File.WriteAllText(ini, "[/Script/Engine.GameUserSettings]\nEnabledMods=1\n\n[/Script/AbioticFactor.AbioticGameUserSettings]\n" + lines);

        Assert.Equal(expected, ModIoInstallLocator.ReadGameEnabledModsFrom(ini)!.Order());
        Assert.Null(ModIoInstallLocator.ReadGameEnabledModsFrom(Path.Combine(_root, "missing.ini")));
    }

    [Fact]
    public void World_mods_are_compared_with_what_the_editor_loads()
    {
        AfInstallLocator.InstalledMod Mod(long id, string name) => new(ModIoInstallLocator.KeyFor(id), ["x.pak"]) { DisplayName = name, ModIoId = id };
        var installed = new[] { Mod(1, "Linked Deployables"), Mod(2, "Jotun LMG"), Mod(3, "Robocart") };

        var usage = WorldModUsage.For([1, 2, 4, 5], [ModIoInstallLocator.KeyFor(1), ModIoInstallLocator.KeyFor(3), "~mods-pak"],
            installed, [new(4, "Fresh download")]);

        Assert.Equal([WorldModState.Loaded, WorldModState.Off, WorldModState.Downloading, WorldModState.Missing], usage.WorldMods.Select(mod => mod.State));
        Assert.Equal("mod.io #5", usage.WorldMods[3].DisplayName);
        Assert.Equal("Robocart", Assert.Single(usage.ExtraLoaded).DisplayName);
        Assert.False(usage.Matches);

        Assert.True(WorldModUsage.For([1], [ModIoInstallLocator.KeyFor(1)], installed, []).Matches);
        Assert.True(WorldModUsage.For([], [], installed, []).Matches);
    }

    [Fact]
    public void The_world_metadata_save_lists_the_mods_it_last_ran_with()
    {
        var source = CommunityUpdateFiveTests.Fixture("WorldSave_MetaData.sav");
        using var original = File.OpenRead(source);
        var save = SaveGame.LoadFrom(original);
        Assert.Empty(WorldSaveReader.ReadFromSave(save).LastUsedModIds);

        // The game writes LastUsedModIds as a plain Int64 array on AbioticWorldMetadataSave.
        var name = new FString("LastUsedModIds");
        var element = new FPropertyTypeName(new FString(nameof(Int64Property)));
        var type = new FPropertyTypeName(new FString(nameof(ArrayProperty)), [element]);
        save.Properties!.Add(new FPropertyTag(name, type, EPropertyTagFlags.None)
        {
            Property = new ArrayProperty(name, element) { Value = new long[] { 6368513, 6486047, 6368513 } },
        });
        using var written = new MemoryStream();
        save.WriteTo(written);

        written.Position = 0;
        Assert.Equal([6368513L, 6486047L], WorldSaveReader.ReadLastUsedModIds(written));
        written.Position = 0;
        Assert.Equal([6368513L, 6486047L], WorldSaveReader.ReadFromStream(written).LastUsedModIds);
    }

    /// <summary>
    /// Against real downloads laid out like the game's mod.io cache. Skips unless
    /// <c>ABIOTIC_MODIO_ROOT</c> points at one and the game is installed. Checked with Linked
    /// Deployables, Jotun LMG, Paul's Goat Hoof and Robocart.
    /// </summary>
    [Fact]
    public void Real_mod_io_mods_supply_items_pictures_and_placed_object_classes()
    {
        if (Environment.GetEnvironmentVariable(ModIoInstallLocator.RootEnvironmentVariable) is not { Length: > 0 }
            || ModIoInstallLocator.FindInstalled() is not { Count: > 0 } mods
            || AfInstallLocator.FindPaksDirectory() is not { } paks)
        {
            _output.WriteLine("No mod.io cache or game install; skipping.");
            return;
        }
        using var provider = GameAssetProvider.CreateForPaks(paks, mods, mappingsPath: GameAssetProvider.FindConventionalMappings() ?? MappingsInRepo(),
            cacheDir: Path.Combine(_root, "cache"));
        Assert.Equal(mods.Count, provider.LoadedMods.Count);

        var catalog = ItemCatalog.LoadFrom(provider);
        foreach (var item in catalog.Entries.Where(entry => entry.IconAssetPath?.StartsWith("/Game/", StringComparison.Ordinal) == false))
        {
            _output.WriteLine($"{item.Id} '{item.DisplayName}' -> {item.IconAssetPath}");
            Assert.NotNull(provider.ExtractTextureByGameRef(item.IconAssetPath));
        }

        if (mods.Any(mod => mod.ModIoId == 6368513))
        {
            // Linked Deployables swaps the benches' placed class for its own subclass.
            var bench = provider.GetClassFacts("/LinkedBenchPowermFqPC/Deployables/Deployed_CraftingBench_Default_Linked.Deployed_CraftingBench_Default_Linked_C");
            Assert.NotNull(bench);
            Assert.True(bench.IsA("Deployed_CraftingBench_Default_C"));
            Assert.True(bench.FromMod);
            Assert.True(bench.RequiresPower);
            Assert.True(bench.SupportsUpgrades);
            Assert.NotNull(bench.PaintedDeployableRow);
            Assert.NotNull(provider.GetClassFacts("Deployed_Freezer_Linked_C")?.ContainerRequirement);
        }
    }

    private static string? MappingsInRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "assets", "Mappings.usmap");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private string Install(long id, string relativeFile)
    {
        var folder = Path.Combine(_root, "13105", "mods", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var file = Path.Combine(folder, relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "pak");
        return folder;
    }

    private void WriteState(params object[] mods)
    {
        var metadata = Path.Combine(_root, "13105", "metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "state.json"), JsonSerializer.Serialize(new { Mods = mods, version = 1 }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
