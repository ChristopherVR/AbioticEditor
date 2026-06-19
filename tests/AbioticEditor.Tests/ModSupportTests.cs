using System.IO;
using AbioticEditor.Core.Assets;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Mod support: the mod-pak discovery on disk, the struct-based table discovery's name-shape
/// gate, and the load toggle. The file-IO/predicate pieces are deterministic; the parts that
/// need a real install skip gracefully when one isn't present (matching the other asset tests).
/// </summary>
public sealed class ModSupportTests
{
    private readonly ITestOutputHelper _output;

    public ModSupportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ---------- FindModPaks: only subfolder paks count as mods ----------

    [Fact]
    public void FindModPaks_ReturnsOnlySubfolderPaks()
    {
        using var tmp = new TempDir();
        var paks = Directory.CreateDirectory(Path.Combine(tmp.Path, "Paks")).FullName;
        File.WriteAllText(Path.Combine(paks, "pakchunk0-Windows.pak"), "base"); // top-level = base game
        var mods = Directory.CreateDirectory(Path.Combine(paks, "~mods")).FullName;
        File.WriteAllText(Path.Combine(mods, "CoolWeapons.pak"), "mod");
        var logic = Directory.CreateDirectory(Path.Combine(paks, "LogicMods")).FullName;
        File.WriteAllText(Path.Combine(logic, "QoLTweaks.utoc"), "mod");
        File.WriteAllText(Path.Combine(logic, "readme.txt"), "not a pak");

        var found = AfInstallLocator.FindModPaks(paks).Select(Path.GetFileName).ToList();

        Assert.Equal(2, found.Count);
        Assert.Contains("CoolWeapons.pak", found);
        Assert.Contains("QoLTweaks.utoc", found);
        Assert.DoesNotContain("pakchunk0-Windows.pak", found); // top-level pak is base, not a mod
        Assert.DoesNotContain("readme.txt", found);            // non-pak/utoc files ignored
    }

    [Fact]
    public void FindModPaks_EmptyWhenNoModFolders()
    {
        using var tmp = new TempDir();
        var paks = Directory.CreateDirectory(Path.Combine(tmp.Path, "Paks")).FullName;
        File.WriteAllText(Path.Combine(paks, "pakchunk0.pak"), "base");

        Assert.Empty(AfInstallLocator.FindModPaks(paks));
        Assert.Empty(AfInstallLocator.FindModPaks(null));
        Assert.Empty(AfInstallLocator.FindModPaks(Path.Combine(tmp.Path, "does-not-exist")));
    }

    [Fact]
    public void FindMods_GroupsFilesByStemIntoOneMod()
    {
        using var tmp = new TempDir();
        var paks = Directory.CreateDirectory(Path.Combine(tmp.Path, "Paks")).FullName;
        var mods = Directory.CreateDirectory(Path.Combine(paks, "~mods")).FullName;
        // An IoStore mod ships .pak + .utoc (+ .ucas) sharing a stem -> one mod, the pak/utoc grouped.
        File.WriteAllText(Path.Combine(mods, "CoolWeapons.pak"), "m");
        File.WriteAllText(Path.Combine(mods, "CoolWeapons.utoc"), "m");
        File.WriteAllText(Path.Combine(mods, "CoolWeapons.ucas"), "m"); // not a registerable entry
        var logic = Directory.CreateDirectory(Path.Combine(paks, "LogicMods")).FullName;
        File.WriteAllText(Path.Combine(logic, "QoLTweaks.pak"), "m");

        var found = AfInstallLocator.FindMods(paks);

        Assert.Equal(2, found.Count);
        var cool = found.Single(m => m.Name == "CoolWeapons");
        Assert.Equal(2, cool.Files.Count); // .pak + .utoc (the .ucas is opened automatically)
        Assert.Contains(found, m => m.Name == "QoLTweaks");
    }

    // ---------- ModTableDiscovery name-shape gate ----------

    [Theory]
    [InlineData("AbioticFactor/Content/Blueprints/Items/ItemTable_Global.uasset", true)]
    [InlineData("MyMod/Content/DataTables/DT_ModItems.uasset", true)]
    [InlineData("AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits.uasset", true)]
    [InlineData("MyMod/Content/DataTable_Things.uasset", true)]
    // Not a data-table name, or not a .uasset.
    [InlineData("AbioticFactor/Content/Textures/GUI/Icon_Foo.uasset", false)]
    [InlineData("MyMod/Content/Blueprints/BP_Thing.uasset", false)]
    [InlineData("MyMod/Content/DataTables/DT_ModItems.uexp", false)]
    public void LooksLikeDataTable_GatesCandidatesByName(string assetPath, bool expected)
    {
        Assert.Equal(expected, ModTableDiscovery.LooksLikeDataTable(assetPath));
    }

    // ---------- ModLoadStore: env var override ----------

    [Fact]
    public void ModsEnabled_ForcedOffByEnvVar()
    {
        using (new EnvScope(ModLoadStore.DisableEnvVar, "1"))
        {
            Assert.True(ModLoadStore.DisabledByEnv);
            Assert.False(ModLoadStore.ModsEnabled);
        }

        // Clearing the env var restores the persisted decision (default true).
        using (new EnvScope(ModLoadStore.DisableEnvVar, null))
        {
            Assert.False(ModLoadStore.DisabledByEnv);
            Assert.Equal(ModLoadStore.PersistedEnabled, ModLoadStore.ModsEnabled);
        }
    }

    // ---------- ModLoadStore: per-mod enable/disable ----------

    [Fact]
    public void SetModEnabled_RoundTripsThroughDisabledSet()
    {
        // The store writes to a real per-user file; preserve and restore it so the test is hermetic.
        var path = ModLoadStore.DisabledModsPath;
        var hadFile = File.Exists(path);
        var original = hadFile ? File.ReadAllText(path) : null;
        try
        {
            var name = "UnitTestMod_" + Guid.NewGuid().ToString("N");
            Assert.True(ModLoadStore.IsModEnabled(name)); // unknown mods default to enabled

            ModLoadStore.SetModEnabled(name, false);
            Assert.False(ModLoadStore.IsModEnabled(name));
            Assert.Contains(name, ModLoadStore.DisabledMods);

            ModLoadStore.SetModEnabled(name, true);
            Assert.True(ModLoadStore.IsModEnabled(name));
            Assert.DoesNotContain(name, ModLoadStore.DisabledMods);
        }
        finally
        {
            if (original is not null) File.WriteAllText(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---------- live install: base-only mounts no mods ----------

    [Fact]
    public void BaseOnlyProvider_ReportsNoMods()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is null)
        {
            _output.WriteLine("No install; skipping.");
            return;
        }
        Assert.Empty(provider.LoadedMods);
    }

    [Fact]
    public void DefaultProvider_LoadedModsMatchesEnabledModsOnDisk()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: true);
        if (provider is null)
        {
            _output.WriteLine("No install; skipping.");
            return;
        }

        // LoadedMods is the set of enabled mod names actually mounted.
        var paks = AfInstallLocator.FindPaksDirectory();
        var expected = AfInstallLocator.FindMods(paks)
            .Where(m => ModLoadStore.IsModEnabled(m.Name))
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();
        var actual = provider.LoadedMods.OrderBy(n => n).ToList();
        _output.WriteLine($"enabled mods on disk: {string.Join(", ", expected)}");
        Assert.Equal(expected, actual);
    }

    // ---------- helpers ----------

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Path);

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-mod-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
