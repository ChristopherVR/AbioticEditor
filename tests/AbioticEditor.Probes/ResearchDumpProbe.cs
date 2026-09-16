using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Saves;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe (session 2026-09-17, see <c>docs/reference/research/</c>) grounding two
/// features: which crop a garden plot's planting spot stores, and what a carried pet's saved
/// <c>PetMutation</c> int actually encodes. Discovery output only, not part of normal test runs.
/// </summary>
public class ResearchDumpProbe
{
    private readonly ITestOutputHelper _output;

    public ResearchDumpProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DefaultFileProvider? CreateRawProvider()
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

    /// <summary>Dumps every DT_Pets row's DefaultParent/Mutations_ so the pest/Skink family
    /// mutation lists (and their MutationTarget_ ordering) are visible in raw form.</summary>
    [Fact]
    public void Dump_PetMutations_AcrossAllDTPetsRows()
    {
        using var provider = CreateRawProvider();
        if (provider is null) { _output.WriteLine("No local install found - skipping."); return; }

        var table = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_Pets").GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"DT_Pets: {table.RowMap.Count} rows");
        foreach (var entry in table.RowMap)
        {
            var parent = entry.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("DefaultParent_", StringComparison.Ordinal));
            var mutations = entry.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("Mutations_", StringComparison.Ordinal));
            _output.WriteLine($"=== {entry.Key.Text} ===");
            _output.WriteLine($"  DefaultParent = {Format(parent?.Tag?.GenericValue, 0)}");
            _output.WriteLine($"  Mutations_ = {Format(mutations?.Tag?.GenericValue, 0)}");
        }
    }

    /// <summary>Confirms <c>PetCareCatalog.MutationOptionsFor</c> resolves the exact two carried
    /// pets found across every fixture (<c>PetMutationProgressProbe</c>) back to their own
    /// current identity: a Leyak Pest (PetMutation=6, index 5 of 0..6 in the base "pest" row's
    /// list) and a crafted Magma Skink (PetMutation=1, index 0 of 0..1 in "Skink_Crafted"'s
    /// separate weapon-form list, not "Skink"'s own list).</summary>
    [Fact]
    public void Verify_MutationOptionsFor_MatchesObservedFixtureValues()
    {
        var gameAssetProvider = GameAssetProvider.CreateForLocalInstall();
        if (gameAssetProvider is null) { _output.WriteLine("No local install found - skipping."); return; }
        AbioticEditor.Core.WorldSaves.PetCatalog.ApplyGameData(AbioticEditor.Core.WorldSaves.PetGameData.TryLoadFrom(gameAssetProvider));

        foreach (var (itemRow, observed) in new[] { ("Pest_Leyak", 6), ("Skink_Magma_Crafted", 1) })
        {
            var options = AbioticEditor.Core.WorldSaves.PetCareCatalog.MutationOptionsFor(gameAssetProvider, itemRow);
            _output.WriteLine($"=== {itemRow} (observed PetMutation={observed}) ===");
            foreach (var o in options) _output.WriteLine($"  {o.Value}: target={o.TargetRow} display={o.DisplayName} foods=[{string.Join(",", o.Foods)}]");
            var match = options.FirstOrDefault(o => o.Value == observed);
            _output.WriteLine(match is not null ? $"  MATCH: observed value resolves to {match.TargetRow}" : "  NO MATCH FOUND");
        }
    }

    /// <summary>Dumps every garden plot's <c>ItemProxies_</c> (spot index + <c>ItemRow_</c>
    /// RowName) across every fixture world save, to ground which item rows a real save actually
    /// plants.</summary>
    [Fact]
    public void Dump_GardenPlotItemProxies_AcrossFixtureWorlds()
    {
        var roots = new[] { Fixtures.ServerWorldsDir, Fixtures.CascadeDir }
            .Where(d => d is not null).Select(d => d!).ToList();
        roots.AddRange(Fixtures.ClientWorldSaves("WorldSave_*.sav").Select(System.IO.Path.GetDirectoryName).Where(d => d is not null).Select(d => d!).Distinct());
        if (roots.Count == 0) { _output.WriteLine("no fixtures"); return; }

        foreach (var root in roots.Distinct())
        {
            foreach (var f in Directory.EnumerateFiles(root, "WorldSave_*.sav", SearchOption.AllDirectories))
            {
                SaveGame save;
                try { using var fs = File.OpenRead(f); save = SaveGame.LoadFrom(fs); }
                catch (Exception ex) { _output.WriteLine($"{f}: load failed: {ex.Message}"); continue; }

                var entries = AbioticEditor.Core.WorldSaves.Features.WorldMapAccessor.Entries(save, "DeployedObjectMap")
                    .Where(e => (e.Props.FirstOrDefault(p => p.Name?.Value.StartsWith("Class_", StringComparison.Ordinal) == true)?.Property?.Value?.ToString() ?? "")
                        .Contains("/Farming/GardenPlot_", StringComparison.Ordinal))
                    .ToList();
                if (entries.Count == 0) continue;
                _output.WriteLine($"=== {f} ({entries.Count} garden plots) ===");
                foreach (var e in entries)
                {
                    var proxies = e.Props.FirstOrDefault(p => p.Name?.Value.StartsWith("ItemProxies_", StringComparison.Ordinal) == true)?.Property as ArrayProperty;
                    if (proxies?.Value is null) { _output.WriteLine("  (no ItemProxies_)"); continue; }
                    foreach (var el in proxies.Value.OfType<StructProperty>())
                    {
                        if (el.Value is not PropertiesStruct ps) continue;
                        var spot = ps.Properties.FirstOrDefault(p => p.Name?.Value.StartsWith("SpotIndex_", StringComparison.Ordinal) == true)?.Property?.Value;
                        var itemRowTag = ps.Properties.FirstOrDefault(p => p.Name?.Value.StartsWith("ItemRow_", StringComparison.Ordinal) == true)?.Property;
                        string rowDump = "(none)";
                        if (itemRowTag is StructProperty rowSp && rowSp.Value is PropertiesStruct rowPs)
                        {
                            rowDump = string.Join(", ", rowPs.Properties.Select(p => $"{p.Name?.Value}={p.Property?.Value}"));
                        }
                        _output.WriteLine($"  spot={spot} ItemRow_={{ {rowDump} }}");
                    }
                }
            }
        }
    }

    /// <summary>Dumps every <c>Plant_</c>-prefixed ItemTable_Global row's GameplayTags_, which is
    /// how real crops (tagged <c>Item.Material.Biological, Item.Plant</c>) are told apart from the
    /// unrelated "digital farm plot" ammo cartridges that also use the <c>Plant_</c> prefix
    /// (tagged only <c>Item.Plant</c>, e.g. <c>Plant_Blank</c>/<c>Plant_9mm</c>/<c>Plant_Pepper</c>
    /// for <c>Deployed_GardenPlot_Digital</c>, a separate class the garden-plot feature's
    /// <c>/Farming/GardenPlot_</c> class filter already excludes).</summary>
    [Fact]
    public void Dump_ItemTableGlobal_PlantRowTags()
    {
        using var provider = CreateRawProvider();
        if (provider is null) { _output.WriteLine("No local install found - skipping."); return; }

        var items = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global").GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"ItemTable_Global: {items.RowMap.Count} rows");
        foreach (var kv in items.RowMap.Where(kv => kv.Key.Text.StartsWith("Plant_", StringComparison.OrdinalIgnoreCase)))
        {
            var tags = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("GameplayTags_", StringComparison.Ordinal));
            var mesh = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("WorldStaticMesh_", StringComparison.Ordinal));
            var cookable = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("CookableData_", StringComparison.Ordinal));
            var farmableRow = cookable?.Tag?.GenericValue is FStructFallback cd
                ? cd.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("FarmableDataRow_", StringComparison.Ordinal))
                : null;
            _output.WriteLine($"  {kv.Key.Text}: tags=[{Format(tags?.Tag?.GenericValue, 0)}] mesh={Format(mesh?.Tag?.GenericValue, 0)} farmableRow={Format(farmableRow?.Tag?.GenericValue, 0)}");
        }
    }

    private static string Format(object? value, int depth)
    {
        if (value is null) return "<null>";
        if (depth >= 5) return value.ToString() ?? value.GetType().Name;
        if (value is FScriptStruct scriptStruct)
            return Format(scriptStruct.StructType, depth + 1);
        if (value is FStructFallback structure)
            return "{ " + string.Join(", ", structure.Properties.Select(
                p => $"{p.Name.Text}: {Format(p.Tag?.GenericValue, depth + 1)}")) + " }";
        if (value is UScriptArray array)
            return "[" + string.Join(", ", array.Properties.Select(p => Format(p.GenericValue, depth + 1))) + "]";
        return value.ToString() ?? value.GetType().Name;
    }
}
