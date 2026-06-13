using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation: what do <c>NarrativeNPCMap</c> entries actually represent, and is
/// "mark as dead" a sensible editor action?
///
/// Findings (see dotnet/docs/research-narrative-npcs.md):
/// - Map keys are level actor paths, almost all generic (<c>Human_ParentBP</c> /
///   <c>Human_Hologram</c>) - the save rarely identifies a specific character.
/// - <c>E_NarrativeNPCStates</c> has 6 values (0–5) with no friendly names; it is a
///   per-NPC dialogue/script phase (live entries are virtually all 3; CDO default 2;
///   <c>Trigger_NarrativeNPCUpdate</c> pushes new phases), not questline progress.
/// - <c>IsDead=true</c> is mostly written by story scripting (the trigger has
///   <c>KillNPC?</c>/<c>DeleteNPC?</c>); players cannot kill most of these NPCs. The
///   only user-meaningful edit is revive (IsDead=false, state back to 3).
/// </summary>
public class NarrativeNpcProbeTests
{
    private readonly ITestOutputHelper _output;

    public NarrativeNpcProbeTests(ITestOutputHelper output)
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
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    /// <summary>
    /// Dumps every NarrativeNPCMap entry (all raw fields, recursively) across every
    /// WorldSave_*.sav in the fixture folder.
    /// </summary>
    [Fact]
    public void Dump_NarrativeNpcMap_AllWorldSaves()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var total = 0;
        foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir!, "WorldSave_*.sav").OrderBy(p => p))
        {
            using var fs = File.OpenRead(path);
            var save = SaveGame.LoadFrom(fs);
            var tag = save.Properties?.FirstOrDefault(t => t.Name!.Value.StartsWith("NarrativeNPCMap"));
            if (tag?.Property is not MapProperty map || map.Value is null || map.Value.Count == 0) continue;

            _output.WriteLine($"=== {Path.GetFileName(path)} ({map.Value.Count} entries) ===");
            foreach (var kvp in map.Value)
            {
                total++;
                _output.WriteLine($"KEY {kvp.Key.Value}");
                DumpProperty("value", kvp.Value, 1);
            }
        }
        _output.WriteLine($"TOTAL entries across fixture: {total}");
    }

    private void DumpProperty(string label, object? value, int depth)
    {
        var pad = new string(' ', depth * 2);
        switch (value)
        {
            case StructProperty sp:
                DumpProperty(label, sp.Value, depth);
                break;
            case PropertiesStruct ps:
                _output.WriteLine($"{pad}{label}:");
                foreach (var p in ps.Properties)
                    DumpProperty(p.Name!.Value, p.Property, depth + 1);
                break;
            case VectorStruct vec:
                _output.WriteLine($"{pad}{label} = ({vec.Value.X:F0}, {vec.Value.Y:F0}, {vec.Value.Z:F0})");
                break;
            case MapProperty mp:
                _output.WriteLine($"{pad}{label} (map, {mp.Value?.Count ?? 0}):");
                if (mp.Value is not null)
                {
                    foreach (var kvp in mp.Value)
                        DumpProperty(kvp.Key.Value?.ToString() ?? "(null)", kvp.Value, depth + 1);
                }
                break;
            case ArrayProperty ap:
                _output.WriteLine($"{pad}{label}[{ap.Value?.Length ?? 0}]:");
                if (ap.Value is not null)
                {
                    for (var i = 0; i < ap.Value.Length; i++)
                        DumpProperty($"[{i}]", ap.Value.GetValue(i), depth + 1);
                }
                break;
            case UeSaveGame.FProperty fp:
                DumpProperty(label, fp.Value, depth);
                break;
            default:
                var s = value?.ToString() ?? "(null)";
                // SoftObjectPathStruct is internal to UeSaveGame - unwrap via reflection.
                if (value is not null && s == value.GetType().ToString()
                    && value.GetType().GetProperty("Value")?.GetValue(value) is UeSaveGame.DataTypes.SoftObjectPath sop)
                {
                    s = $"{sop.PackageName}.{sop.AssetName}:{sop.SubPathString}";
                }
                _output.WriteLine($"{pad}{label} = {s}");
                break;
        }
    }

    /// <summary>Enumerates E_NarrativeNPCStates values with their numeric ordinals.</summary>
    [Fact]
    public void Dump_NarrativeNpcStates_Enum()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Data/E_NarrativeNPCStates");
        foreach (var e in pkg.GetExports().OfType<UEnum>())
        {
            _output.WriteLine($"ENUM {e.Name} ({e.Names.Length} entries incl. MAX)");
            foreach (var (name, ordinal) in e.Names)
            {
                _output.WriteLine($"  {ordinal}: {name.Text}");
            }
        }
    }

    /// <summary>
    /// Lists every cooked asset whose path mentions NarrativeNPC, then dumps the
    /// default-object properties of the human narrative-NPC blueprints to see how the
    /// state machine and trader identity are wired.
    /// </summary>
    [Fact]
    public void Dump_NarrativeNpcAssets_AndBlueprintDefaults()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var keys = provider.Files.Keys
            .Where(k => k.Contains("NarrativeNPC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k)
            .ToList();
        var nonAudio = keys.Where(k => !k.Contains("/Audio/", StringComparison.OrdinalIgnoreCase)).ToList();
        _output.WriteLine($"FILES: {keys.Count} total ({keys.Count - nonAudio.Count} dialogue audio omitted)");
        foreach (var k in nonAudio) _output.WriteLine($"  {k}");

        // Dump default property values of the gameplay blueprints (state machine and
        // trader identity live here, not in the audio assets).
        var interesting = nonAudio.Where(k =>
            k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && (k.Contains("Characters/NarrativeNPCs/", StringComparison.OrdinalIgnoreCase)
                || k.Contains("NarrativeNPCDirector", StringComparison.OrdinalIgnoreCase)
                || k.Contains("Trigger_NarrativeNPCUpdate", StringComparison.OrdinalIgnoreCase)
                || k.Contains("NarrativeNPCSpawns_Struct", StringComparison.OrdinalIgnoreCase)));
        foreach (var key in interesting)
        {
            CUE4Parse.UE4.Assets.IPackage pkg;
            try
            {
                pkg = provider.LoadPackage(key);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"--- {key}: load failed ({ex.GetType().Name})");
                continue;
            }

            _output.WriteLine($"--- {key}");
            foreach (var export in pkg.GetExports())
            {
                if (export is UStruct st && st.ChildProperties is { Length: > 0 })
                {
                    _output.WriteLine($"  STRUCT {export.Name}: " +
                        string.Join(", ", st.ChildProperties.Select(c => c.Name.Text)));
                }
                if (!export.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                _output.WriteLine($"  CDO {export.Name} ({export.Class?.Name})");
                foreach (var p in export.Properties)
                {
                    var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                    if (v.Length > 140) v = v[..140];
                    _output.WriteLine($"    {p.Name.Text} = {v}");
                }
            }
        }
    }
}
