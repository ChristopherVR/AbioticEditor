using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Discovery probe for the fish-journal rework: what every DT_Fish row exposes about
/// (a) what catching the fish unlocks (bait, recipes) and (b) what is required to catch
/// it (conditions, items, bait, location). Dumps the full property set.
/// </summary>
public class FishSchemaProbeTests
{
    private readonly ITestOutputHelper _output;

    public FishSchemaProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return null;
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is not null)
            provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    [Fact]
    public void Dump_FishFullSchema()
    {
        using var provider = CreateProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("no install/mappings"); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Fishing/DT_Fish");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"DT_Fish: {dt.RowMap.Count} rows");

            // Column union across all rows.
            var cols = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var kv in dt.RowMap)
                foreach (var p in kv.Value.Properties)
                    cols.Add(p.Name.Text);
            _output.WriteLine("COLUMNS: " + string.Join(", ", cols));

            // Full dump of a handful of rows incl. a likely "rare/conditional" one.
            string[] wanted = ["Bass", "GarbageEel", "Carp", "Anglerfish", "VoidRay", "Gemcrab"];
            var sample = dt.RowMap.Where(kv => wanted.Any(w => kv.Key.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .Concat(dt.RowMap)
                .DistinctBy(kv => kv.Key.Text)
                .Take(8);
            foreach (var kv in sample)
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    DumpProp("  ", p.Name.Text, p.Tag?.GenericValue);
                }
            }
        }
    }

    [Fact]
    public void Dump_FishTimeAndCatchRequirements()
    {
        using var provider = CreateProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("no install/mappings"); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Fishing/DT_Fish");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            foreach (var kv in dt.RowMap)
            {
                var tod = AsStruct(kv.Value.Properties.FirstOrDefault(p => p.Name.Text == "TimeOfDayCatchChance")?.Tag?.GenericValue);
                var catchReq = AsStruct(kv.Value.Properties.FirstOrDefault(p => p.Name.Text == "CatchRequirement")?.Tag?.GenericValue);
                var todHas = tod is { Properties.Count: > 0 };
                var tags = catchReq?.Properties.FirstOrDefault(p => p.Name.Text == "TagDictionary")?.Tag?.GenericValue is CUE4Parse.UE4.Assets.Objects.UScriptArray { Properties.Count: > 0 };
                if (!todHas && !tags) continue;
                _output.WriteLine($"=== {kv.Key.Text} ===");
                if (tod is not null)
                {
                    _output.WriteLine("  TimeOfDayCatchChance:");
                    foreach (var p in tod.Properties) DumpProp("      ", p.Name.Text, p.Tag?.GenericValue);
                }
                if (catchReq is not null)
                {
                    _output.WriteLine("  CatchRequirement:");
                    foreach (var p in catchReq.Properties) DumpProp("      ", p.Name.Text, p.Tag?.GenericValue);
                }
            }
        }
    }

    private static CUE4Parse.UE4.Assets.Objects.FStructFallback? AsStruct(object? value)
    {
        if (value is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) value = ss.StructType;
        return value as CUE4Parse.UE4.Assets.Objects.FStructFallback;
    }

    [Fact]
    public void Dump_FishCatchTagsAndTimeFields()
    {
        using var provider = CreateProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("no install/mappings"); return; }

        // Full time-of-day field set for a fish that has a non-trivial profile.
        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Fishing/DT_Fish");
        var dt = pkg.GetExports().OfType<UDataTable>().First();
        foreach (var name in new[] { "DarkwaterFish", "Eel_rare3", "MoonFish" })
        {
            var row = dt.RowMap.FirstOrDefault(kv => kv.Key.Text == name).Value;
            if (row is null) continue;
            var tod = AsStruct(row.Properties.FirstOrDefault(p => p.Name.Text == "TimeOfDayCatchChance")?.Tag?.GenericValue);
            _output.WriteLine($"--- {name} TimeOfDay full ---");
            if (tod is not null) foreach (var p in tod.Properties) DumpProp("    ", p.Name.Text, p.Tag?.GenericValue);
        }

        // Every distinct catch-requirement tag across all rows (to spot non-bait/equipment tags).
        var allTags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var kv in dt.RowMap)
        {
            var req = AsStruct(kv.Value.Properties.FirstOrDefault(p => p.Name.Text == "CatchRequirement")?.Tag?.GenericValue);
            var tagArr = req?.Properties.FirstOrDefault(p => p.Name.Text == "TagDictionary")?.Tag?.GenericValue as CUE4Parse.UE4.Assets.Objects.UScriptArray;
            if (tagArr is null) continue;
            var names = new List<string>();
            foreach (var e in tagArr.Properties)
            {
                var tag = AsStruct(e.GenericValue)?.Properties.FirstOrDefault(p => p.Name.Text == "TagName")?.Tag?.GenericValue?.ToString();
                if (tag is not null) { names.Add(tag); allTags.Add(tag); }
            }
            if (names.Count > 1 || names.Any(t => !t.StartsWith("Fishing.Bait", StringComparison.Ordinal)))
                _output.WriteLine($"NON-BAIT/MULTI {kv.Key.Text}: {string.Join(" + ", names)}");
        }
        _output.WriteLine("ALL CATCH TAGS: " + string.Join(", ", allTags));

        // RecipeToUnlock for every fish row (resolution validated separately in CodexTests).
        foreach (var kv in dt.RowMap)
        {
            var rec = AsStruct(kv.Value.Properties.FirstOrDefault(p => p.Name.Text == "RecipeToUnlock")?.Tag?.GenericValue)
                ?.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
            if (!string.IsNullOrEmpty(rec) && rec != "None")
                _output.WriteLine($"UNLOCK {kv.Key.Text}: {rec}");
        }
    }

    [Fact]
    public void Dump_FishingRelatedTables()
    {
        using var provider = CreateProvider();
        if (provider is null) return;
        foreach (var f in provider.Files.Keys
            .Where(p => p.Contains("Fishing", StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p))
        {
            _output.WriteLine(f);
        }
    }

    private void DumpProp(string indent, string name, object? value)
    {
        if (value is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) value = ss.StructType;
        if (value is CUE4Parse.UE4.Assets.Objects.FStructFallback sf)
        {
            if (sf.Properties.Count == 0)
            {
                _output.WriteLine($"{indent}{name} = (empty struct)");
                return;
            }
            _output.WriteLine($"{indent}{name}:");
            foreach (var p in sf.Properties)
                DumpProp(indent + "    ", p.Name.Text, p.Tag?.GenericValue);
            return;
        }
        if (value is CUE4Parse.UE4.Assets.Objects.UScriptArray arr)
        {
            _output.WriteLine($"{indent}{name} [array {arr.Properties.Count}]:");
            foreach (var e in arr.Properties)
                DumpProp(indent + "    ", "-", e.GenericValue);
            return;
        }
        if (value is CUE4Parse.UE4.Assets.Objects.UScriptMap map)
        {
            _output.WriteLine($"{indent}{name} [map {map.Properties.Count}]:");
            foreach (var e in map.Properties)
                _output.WriteLine($"{indent}    {Render(e.Key?.GenericValue)} => {Render(e.Value?.GenericValue)}");
            return;
        }
        var v = Render(value);
        if (v.Length > 200) v = v[..200] + "…";
        _output.WriteLine($"{indent}{name} = {v}");
    }

    private static string Render(object? v)
    {
        if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
        return v?.ToString() ?? "(null)";
    }
}
