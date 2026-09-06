using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for live editing: dumps the reflected property and function names of the
/// blueprint classes behind doors, deployed containers, the day/night + weather manager, and
/// the game state / game mode / game instance (where world flags might live), so a live UObject
/// access path can be grounded in the game's own class layout instead of guessed.
/// Output-only. Writes a full dump to LIVE_CLASS_PROBE_OUT when set.
/// </summary>
public class LiveClassPropsProbe
{
    private readonly ITestOutputHelper _output;

    public LiveClassPropsProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;

#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    [Fact]
    public void Dump_LiveEditingClassLayouts()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no paks/mappings; skipping"); return; }

        string[] fragments =
        [
            "DayNightManager", "Abiotic_Survival_GameState", "Abiotic_Survival_GameMode",
            "Abiotic_GameInstance", "SimpleDoor_ParentBP", "SecurityDoor", "AbioticDeployed_ParentBP",
            "Abiotic_InventoryComponent", "AI_Director", "Deployed_Storage", "Deployed_Container",
            "Abiotic_PlayerController", "WorldFlag", "Abiotic_PlayerCharacter.", "AbioticDeployed_Furniture_ParentBP",
            "Abiotic_PlayerState", "WorldSave", "Abiotic_WorldSave", "Deployed_Chest", "Deployed_Locker", "Abiotic_Item_Dropped", "E_DoorStates", "Abiotic_Item_ParentBP.",
            "Abiotic_InventoryChangeableDataStruct", "Abiotic_InventoryItemSlotStruct", "EDynamicProperty",
            "PunchCardTerminal", "Deployed_PunchCardTerminal", "Item_Pet", "Companion",
        ];

        var outPath = Environment.GetEnvironmentVariable("LIVE_CLASS_PROBE_OUT");
        using var writer = outPath is null ? null : new StreamWriter(outPath, false);
        void W(string s) { _output.WriteLine(s); writer?.WriteLine(s); }

        var keys = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(k => fragments.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        W($"matching packages: {keys.Count}");
        foreach (var key in keys)
        {
            CUE4Parse.UE4.Assets.IPackage pkg;
            try { pkg = provider.LoadPackage(key[..^".uasset".Length]); }
            catch (Exception ex) { W($"--- {key}: load failed ({ex.GetType().Name}: {ex.Message})"); continue; }

            W($"--- {key}");
            foreach (var export in pkg.GetExports())
            {
                if (export is UStruct st)
                {
                    W($"  STRUCT {export.Name} ({export.ExportType}) super={st.SuperStruct?.Name}");
                    if (st.ChildProperties is { Length: > 0 })
                    {
                        foreach (var c in st.ChildProperties)
                            W($"    prop {c.Name.Text} : {c.GetType().Name}");
                    }
                    if (st.Children is { Length: > 0 })
                    {
                        foreach (var c in st.Children)
                            W($"    func {c.Name}");
                    }
                }
            }
        }
    }
}

public class LiveNativeClassPropsProbe
{
    private readonly ITestOutputHelper _output;
    public LiveNativeClassPropsProbe(ITestOutputHelper output) { _output = output; }

    /// <summary>Dumps the NATIVE (usmap) property layouts of the C++ Abiotic classes that the
    /// blueprint classes above derive from - the usmap carries properties only, no functions.</summary>
    [Fact]
    public void Dump_NativeAbioticClassLayouts()
    {
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is null) { _output.WriteLine("no mappings"); return; }
        var provider = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        var outPath = Environment.GetEnvironmentVariable("LIVE_NATIVE_PROBE_OUT");
        using var writer = outPath is null ? null : new StreamWriter(outPath, false);
        void W(string s) { _output.WriteLine(s); writer?.WriteLine(s); }
        var types = provider.MappingsForGame!.Types;
        W($"native types: {types.Count}");
        foreach (var (name, st) in types.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            var interesting = name.Contains("Abiotic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WorldFlag", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Weather", StringComparison.OrdinalIgnoreCase);
            var flagProp = st.Properties.Values.Any(p => p.Name.Contains("Flag", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("WorldSave", StringComparison.OrdinalIgnoreCase));
            if (!interesting && !flagProp) continue;
            W($"  NATIVE {name} super={st.SuperType ?? "-"}");
            foreach (var p in st.Properties.Values.OrderBy(p => p.Index))
                W($"    prop {p.Name} : {p.MappingType?.Type}");
        }
    }
}

public class SurvivalStatDefaultsProbe
{
    private readonly ITestOutputHelper _output;
    public SurvivalStatDefaultsProbe(ITestOutputHelper output) { _output = output; }

    /// <summary>What ARE the blueprint defaults of the five survival stats? A stat sitting at its
    /// default is omitted from the save entirely, so the reader's assumed default decides what the
    /// editor writes back for it on every save. Dumps the SurvivalStats struct asset and the
    /// player character CDO.</summary>
    [Fact]
    public void Dump_SurvivalStatDefaults()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) { _output.WriteLine("no paks"); return; }
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var keys = provider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && (k.Contains("SurvivalStat", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith("Blueprints/Characters/Abiotic_PlayerCharacter.uasset", StringComparison.OrdinalIgnoreCase)
                || k.Contains("CharacterSaveData", StringComparison.OrdinalIgnoreCase)
                || k.Contains("Abiotic_CharacterSave", StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var key in keys)
        {
            _output.WriteLine($"--- {key}");
            try
            {
                var pkg = provider.LoadPackage(key[..^".uasset".Length]);
                foreach (var export in pkg.GetExports())
                {
                    if (export is UStruct st && st.ChildProperties is { Length: > 0 })
                        _output.WriteLine($"  STRUCT {export.Name}: {string.Join(", ", st.ChildProperties.Select(c => c.Name.Text))}");
                    foreach (var p in export.Properties)
                    {
                        var n = p.Name.Text;
                        if (!(n.Contains("Survival", StringComparison.OrdinalIgnoreCase) || n.Contains("Fatigue", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("Hunger", StringComparison.OrdinalIgnoreCase) || n.Contains("Continence", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("Sanity", StringComparison.OrdinalIgnoreCase) || n.Contains("Thirst", StringComparison.OrdinalIgnoreCase))) continue;
                        var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                        if (v.Length > 400) v = v[..400];
                        _output.WriteLine($"    {export.Name}.{n} = {v}");
                    }
                }
            }
            catch (Exception ex) { _output.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}

public class CharacterStatsStructDefaultsProbe
{
    private readonly ITestOutputHelper _output;
    public CharacterStatsStructDefaultsProbe(ITestOutputHelper output) { _output = output; }

    /// <summary>The save-side struct behind CurrentSurvivalStats_ is CharacterStatsSave_Struct;
    /// its member defaults decide which values the game delta-omits from the file. Dumps every
    /// export of any package whose path mentions it (and the equip/inventory slot structs, whose
    /// defaults matter for the same reason).</summary>
    [Fact]
    public void Dump_CharacterStatsSaveStructDefaults()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) { _output.WriteLine("no paks"); return; }
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var keys = provider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && (k.Contains("CharacterStats", StringComparison.OrdinalIgnoreCase)
                || k.Contains("StatsSave", StringComparison.OrdinalIgnoreCase))).ToList();
        _output.WriteLine($"packages: {keys.Count}");
        foreach (var key in keys)
        {
            _output.WriteLine($"--- {key}");
            try
            {
                var pkg = provider.LoadPackage(key[..^".uasset".Length]);
                foreach (var export in pkg.GetExports())
                {
                    _output.WriteLine($"  export {export.Name} : {export.ExportType}");
                    if (export is UStruct st && st.ChildProperties is { Length: > 0 })
                        _output.WriteLine($"    members: {string.Join(", ", st.ChildProperties.Select(c => c.Name.Text))}");
                    foreach (var p in export.Properties)
                    {
                        var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                        if (v.Length > 300) v = v[..300];
                        _output.WriteLine($"    {p.Name.Text} = {v}");
                    }
                }
            }
            catch (Exception ex) { _output.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}

public class CharacterStatsStructDefaultValuesProbe
{
    private readonly ITestOutputHelper _output;
    public CharacterStatsStructDefaultValuesProbe(ITestOutputHelper output) { _output = output; }

    private void DumpProps(IEnumerable<FPropertyTag> props, string indent, int depth)
    {
        foreach (var p in props)
        {
            var gv = p.Tag?.GenericValue;
            if (gv is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss && ss.StructType is CUE4Parse.UE4.Assets.Objects.FStructFallback) gv = ss.StructType;
            if (gv is CUE4Parse.UE4.Assets.Objects.FStructFallback fb && depth < 4)
            {
                _output.WriteLine($"{indent}{p.Name.Text} = {{");
                DumpProps(fb.Properties, indent + "  ", depth + 1);
                _output.WriteLine($"{indent}}}");
                continue;
            }
            if (gv is CUE4Parse.UE4.Assets.Objects.UScriptArray arr && depth < 4)
            {
                _output.WriteLine($"{indent}{p.Name.Text} = [{arr.Properties.Count}]");
                var i = 0;
                foreach (var el in arr.Properties)
                {
                    if (el.GenericValue is CUE4Parse.UE4.Assets.Objects.FStructFallback efb)
                    {
                        _output.WriteLine($"{indent}  [{i}] {{");
                        DumpProps(efb.Properties, indent + "    ", depth + 1);
                        _output.WriteLine($"{indent}  }}");
                    }
                    else _output.WriteLine($"{indent}  [{i}] {el.GenericValue}");
                    i++;
                    if (i > 12) { _output.WriteLine($"{indent}  ..."); break; }
                }
                continue;
            }
            var v = gv?.ToString() ?? "(null)";
            if (v.Length > 300) v = v[..300];
            _output.WriteLine($"{indent}{p.Name.Text} = {v}");
        }
    }

    [Fact]
    public void Dump_UserDefinedStructDefaultValues()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) { _output.WriteLine("no paks"); return; }
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        string[] fragments = ["CharacterStatsSave_Struct", "Abiotic_InventoryItemSlotStruct", "Abiotic_InventoryChangeableDataStruct", "CharacterSaveData", "Abiotic_CharacterSave"];
        var keys = provider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && fragments.Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var key in keys)
        {
            _output.WriteLine($"--- {key}");
            try
            {
                var pkg = provider.LoadPackage(key[..^".uasset".Length]);
                foreach (var export in pkg.GetExports())
                {
                    if (export is CUE4Parse.UE4.Objects.Engine.UUserDefinedStruct uds)
                    {
                        _output.WriteLine($"  UDS {export.Name} defaults:");
                        foreach (var p in uds.DefaultProperties ?? [])
                        {
                            var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                            if (v.Length > 300) v = v[..300];
                            _output.WriteLine($"    {p.Name.Text} = {v}");
                        }
                    }
                    else if (export.Name.StartsWith("Default__", StringComparison.Ordinal))
                    {
                        _output.WriteLine($"  CDO {export.Name}:");
                        DumpProps(export.Properties, "    ", 0);
                    }
                }
            }
            catch (Exception ex) { _output.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
