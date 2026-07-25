using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Pak-side research for the containment rework. Answers:
///   - Which blueprint is the containment unit (<c>Deployed_LeyakContainment</c>)?
///   - Is it a player-deployable (crafted item) or a level-placed actor? If the former, the
///     set of "all containment units" is save-derived and cannot be enumerated from the map.
///   - Are there level-placed containment actors in the cooked <c>.umap</c> files (the
///     mechanism <c>DoorLocationResolver</c> uses)?
///   - What do the <c>EDynamicProperty::Generic1/2/3</c> slots on the deployable mean?
///   - Which creature rows can a containment unit hold (Leyak / Krasue / others)?
/// </summary>
public class ContainmentBlueprintProbe
{
    private readonly ITestOutputHelper _output;

    public ContainmentBlueprintProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private DefaultFileProvider? Mount()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) { _output.WriteLine("no game install"); return null; }

#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var mappings = GameAssetProvider.FindConventionalMappings();
        _output.WriteLine($"mappings: {mappings ?? "<none>"}");
        if (mappings is not null && File.Exists(mappings))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappings);
        }
        return provider;
    }

    [Fact]
    public void Probe_ContainmentAssetPaths()
    {
        using var provider = Mount();
        if (provider is null) return;

        foreach (var needle in new[] { "LeyakContainment", "Containment", "Krasue" })
        {
            _output.WriteLine($"=== paths containing '{needle}' ===");
            foreach (var p in provider.Files.Keys
                         .Where(p => p.Contains(needle, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                         .Take(60))
            {
                _output.WriteLine($"  {p}");
            }
        }
    }

    [Fact]
    public void Probe_ContainmentBlueprintProperties()
    {
        using var provider = Mount();
        if (provider is null) return;
        if (provider.MappingsContainer is null) { _output.WriteLine("no mappings - cannot decode"); return; }

        string[] objects =
        [
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment.Deployed_LeyakContainment_C",
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment.Default__Deployed_LeyakContainment_C",
        ];

        foreach (var objectPath in objects)
        {
            _output.WriteLine($"=== {objectPath} ===");
            try
            {
                if (!provider.TryLoadPackageObject(objectPath, out var obj) || obj is null)
                {
                    _output.WriteLine("  (not loadable)");
                    continue;
                }
                DumpObject(obj, "  ", 0);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}");
            }
        }

        string[] packages =
        [
            "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment",
            "/Game/Blueprints/Data/LeyakContainment_Struct",
        ];
        foreach (var package in packages)
        {
            _output.WriteLine($"=== all exports of {package} ===");
            try
            {
                var pkg = provider.LoadPackage(package);
                foreach (var export in pkg.GetExports())
                {
                    _output.WriteLine($"  export {export.Name} : {export.ExportType}");
                    DumpObject(export, "      ", 0);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  !! {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Are containment units level-placed actors (findable in the cooked .umap files) or purely
    /// player-deployed? This is the question that decides whether "every containment unit on the
    /// map" can be enumerated from the game data at all.
    /// </summary>
    [Fact]
    public void Probe_LevelPlacedContainmentActors()
    {
        using var provider = Mount();
        if (provider is null) return;

        var maps = provider.Files.Keys
            .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)
                        && p.Contains("/Maps/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _output.WriteLine($"cooked maps: {maps.Count}");

        var found = 0;
        foreach (var map in maps)
        {
            try
            {
                var pkg = provider.LoadPackage(map);
                foreach (var export in pkg.GetExports())
                {
                    var type = export.ExportType ?? "";
                    if (!type.Contains("Containment", StringComparison.OrdinalIgnoreCase)
                        && !type.Contains("Leyak", StringComparison.OrdinalIgnoreCase)) continue;
                    _output.WriteLine($"  {map} :: {export.Name} ({type})");
                    found++;
                }
            }
            catch
            {
                // Unreadable / requires mappings; not every cooked map decodes.
            }
        }
        _output.WriteLine($"level-placed containment-ish actors: {found}");
    }

    /// <summary>
    /// Which item row crafts into the containment unit, and does any data table enumerate
    /// the creature rows a unit can hold?
    /// </summary>
    [Fact]
    public void Probe_ContainmentItemRowAndCreatureRows()
    {
        using var provider = Mount();
        if (provider is null) return;
        if (provider.MappingsContainer is null) { _output.WriteLine("no mappings"); return; }

        foreach (var tablePath in provider.Files.Keys
                     .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                                 && (p.Contains("Leyak", StringComparison.OrdinalIgnoreCase)
                                     || p.Contains("Krasue", StringComparison.OrdinalIgnoreCase))
                                 && (p.Contains("DT_", StringComparison.OrdinalIgnoreCase)
                                     || p.Contains("Table", StringComparison.OrdinalIgnoreCase)))
                     .Take(20))
        {
            _output.WriteLine($"=== {tablePath} ===");
        }

        // The NPC data table is where creature rows like "Leyak" / "Krasue" would live.
        foreach (var candidate in provider.Files.Keys
                     .Where(p => p.Contains("DT_NPC", StringComparison.OrdinalIgnoreCase)
                                 || p.Contains("NPCData", StringComparison.OrdinalIgnoreCase)
                                 || p.Contains("DT_Creature", StringComparison.OrdinalIgnoreCase))
                     .Take(20))
        {
            _output.WriteLine($"npc-table candidate: {candidate}");
        }
    }

    /// <summary>
    /// The CDO's <c>LeyakContainmentData</c> array: one entry per creature a unit can hold.
    /// This is the table that says what index 0 / index 1 mean.
    /// </summary>
    [Fact]
    public void Probe_LeyakContainmentDataArray()
    {
        using var provider = Mount();
        if (provider is null) return;
        if (provider.MappingsContainer is null) { _output.WriteLine("no mappings"); return; }

        if (!provider.TryLoadPackageObject(
                "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment.Default__Deployed_LeyakContainment_C",
                out var cdo) || cdo is null)
        {
            _output.WriteLine("CDO not loadable");
            return;
        }

        foreach (var prop in cdo.Properties)
        {
            _output.WriteLine($"--- {prop.Name.Text} ({prop.Tag?.GetType().Name})");
            DumpTag(prop.Tag, "      ", 0);
        }
    }

    /// <summary>
    /// The containment blueprint's package name table. The blueprint's bytecode cannot be
    /// decompiled here, but every name it references (variables, enum literals like
    /// <c>Generic1</c>, function names) lands in the name map. Seeing exactly which
    /// <c>EDynamicProperty</c> slots the blueprint mentions is the evidence for what
    /// Generic1/2/3 mean on a saved containment unit.
    /// </summary>
    [Fact]
    public void Probe_ContainmentPackageNameTable()
    {
        using var provider = Mount();
        if (provider is null) return;

        try
        {
            var pkg = (CUE4Parse.UE4.Assets.IoPackage)provider.LoadPackage(
                "/Game/Blueprints/DeployedObjects/Furniture/Deployed_LeyakContainment");
            _output.WriteLine($"name map entries: {pkg.NameMap.Length}");
            foreach (var name in pkg.NameMap.Select(n => n.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                _output.WriteLine($"  {name}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"!! {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void DumpTag(FPropertyTagType? tag, string indent, int depth)
    {
        if (tag is null || depth > 5) return;
        switch (tag.GenericValue)
        {
            case UScriptArray arr:
                _output.WriteLine($"{indent}array x{arr.Properties.Count}");
                for (var i = 0; i < arr.Properties.Count; i++)
                {
                    _output.WriteLine($"{indent}  [{i}]");
                    DumpTag(arr.Properties[i], indent + "    ", depth + 1);
                }
                return;
            case FScriptStruct ss:
                DumpStructValue(ss.StructType, indent, depth);
                return;
            case FStructFallback fb:
                foreach (var p in fb.Properties)
                {
                    _output.WriteLine($"{indent}{p.Name.Text}:");
                    DumpTag(p.Tag, indent + "  ", depth + 1);
                }
                return;
            default:
                var s = tag.GenericValue?.ToString() ?? "(null)";
                if (s.Length > 300) s = s[..300] + "…";
                _output.WriteLine($"{indent}{s}");
                return;
        }
    }

    private void DumpStructValue(IUStruct? value, string indent, int depth)
    {
        if (value is FStructFallback fb)
        {
            foreach (var p in fb.Properties)
            {
                _output.WriteLine($"{indent}{p.Name.Text}:");
                DumpTag(p.Tag, indent + "  ", depth + 1);
            }
            return;
        }
        var s = value?.ToString() ?? "(null)";
        if (s.Length > 300) s = s[..300] + "…";
        _output.WriteLine($"{indent}{s}");
    }

    private void DumpObject(UObject obj, string indent, int depth)
    {
        if (depth > 2) return;
        foreach (var prop in obj.Properties)
        {
            var name = prop.Name.Text;
            var val = prop.Tag?.GenericValue?.ToString() ?? "(null)";
            if (val.Length > 220) val = val[..220] + "…";
            _output.WriteLine($"{indent}{name} = {val}");
        }
    }
}
