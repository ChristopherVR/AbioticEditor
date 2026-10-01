using System.IO;
using System.Linq;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// How the game draws crops on a garden plot, for the 3D view: where each crop's growth-stage meshes
/// are defined (the plant table) and where each planting spot sits on a plot. Output-only.
/// </summary>
public class GardenCropModelProbe
{
    private readonly ITestOutputHelper _output;
    public GardenCropModelProbe(ITestOutputHelper output) => _output = output;

    /// <summary>Every plant-related data table: class, parent tables, row count and one row's fields.</summary>
    [Fact]
    public void Dump_PlantTables()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) { _output.WriteLine("no game"); return; }
        foreach (var path in assets.AssetPaths.Where(p => p.Contains("Plant", StringComparison.OrdinalIgnoreCase)
                     && p.Contains("DataTable", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(path, out var pkg)) { _output.WriteLine($"{path}: not loadable"); return 0; }
                foreach (var e in pkg.GetExports().OfType<UDataTable>())
                {
                    var parents = e.GetOrDefault<FPackageIndex[]>("ParentTables", []).Select(x => x.ResolvedObject?.GetPathName()).ToList();
                    _output.WriteLine($"{path}: {e.ExportType} rows={e.RowMap.Count} struct={e.RowStructName} parents=[{string.Join(", ", parents)}]");
                    var rowStruct = e.GetOrDefault<FPackageIndex?>("RowStruct");
                    _output.WriteLine($"  RowStruct {rowStruct?.ResolvedObject?.GetPathName()} class {rowStruct?.ResolvedObject?.Class?.Name}; props: {string.Join(", ", e.Properties.Select(x => x.Name.Text))}");
                    var mapped = p.MappingsForGame?.Types.ContainsKey(e.RowStructName ?? "") ?? false;
                    _output.WriteLine($"  struct in mappings: {mapped}; size {pkg.GetType().Name}");
                    foreach (var (name, row) in e.RowMap.Take(1))
                    {
                        _output.WriteLine($"  row {name.Text}:");
                        foreach (var prop in row.Properties) _output.WriteLine($"    {prop.Name.Text} = {Describe(prop.Tag?.GenericValue)}");
                    }
                    _output.WriteLine($"  rows: {string.Join(", ", e.RowMap.Keys.Select(k => k.Text).Take(60))}");
                }
                return 0;
            });
        }
    }

    /// <summary>The garden plot blueprints' construction-script nodes (spot markers) and plant-related functions.</summary>
    [Fact]
    public void Dump_GardenPlotLayout()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var filter = Environment.GetEnvironmentVariable("ABIOTIC_BP_FILTER");
        foreach (var path in assets.AssetPaths.Where(p => (filter is not null
                     ? p.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     : p.Contains("/Farming/", StringComparison.OrdinalIgnoreCase) && (p.Contains("GardenPlot", StringComparison.OrdinalIgnoreCase) || p.Contains("Plant", StringComparison.OrdinalIgnoreCase)))
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)).Take(30))
        {
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(path, out var pkg)) return 0;
                var exports = pkg.GetExports().ToList();
                var cls = exports.OfType<UBlueprintGeneratedClass>().FirstOrDefault();
                _output.WriteLine($"{path}: {(cls is null ? "no class" : "class " + cls.Name + " super " + cls.SuperStruct?.Name)}");
                foreach (var node in exports.OfType<USCS_Node>())
                {
                    var t = node.ComponentTemplate?.Load();
                    var loc = t?.GetOrDefault("RelativeLocation", CUE4Parse.UE4.Objects.Core.Math.FVector.ZeroVector);
                    var mesh = t?.GetOrDefault<FPackageIndex?>("StaticMesh")?.ResolvedObject?.Name.Text;
                    _output.WriteLine($"   node {node.InternalVariableName.Text,-28} {t?.ExportType,-26} at {loc} parent {node.GetOrDefault<FName>("ParentComponentOrVariableName").Text} mesh {mesh}");
                }
                var defaults = cls?.ClassDefaultObject.Load();
                if (defaults is not null)
                {
                    foreach (var prop in defaults.Properties.Where(x => !x.Name.Text.StartsWith("UberGraph", StringComparison.Ordinal)))
                        _output.WriteLine($"   cdo {prop.Name.Text} = {Describe(prop.Tag?.GenericValue)}");
                }
                var functions = exports.OfType<UFunction>().Select(f => f.Name).Where(n => !n.StartsWith("ExecuteUbergraph", StringComparison.Ordinal));
                _output.WriteLine($"   functions: {string.Join(", ", functions)}");
                return 0;
            });
        }
    }

    private static string Describe(object? value, int depth = 0)
    {
        switch (value)
        {
            case null: return "null";
            case FScriptStruct { StructType: FStructFallback f } when depth < 3:
                return "{" + string.Join(", ", f.Properties.Select(p => $"{p.Name.Text}={Describe(p.Tag?.GenericValue, depth + 1)}")) + "}";
            case UScriptArray a when depth < 3:
                return "[" + string.Join(", ", a.Properties.Take(8).Select(p => Describe(p.GenericValue, depth + 1))) + (a.Properties.Count > 8 ? $", ... {a.Properties.Count}" : "") + "]";
            default:
                var text = value.ToString() ?? "";
                return text.Length > 220 ? text[..220] + "..." : text;
        }
    }

    /// <summary>Non-asset files in the paks whose names mention plants or end in .json (the table loads from JSON).</summary>
    [Fact]
    public void Dump_JsonFiles()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var json = assets.AssetPaths.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        _output.WriteLine($"{json.Count} json files");
        foreach (var p in json.Where(p => p.Contains("Plant", StringComparison.OrdinalIgnoreCase) || p.Contains("DataTable", StringComparison.OrdinalIgnoreCase)).Take(40)) _output.WriteLine("  " + p);
        foreach (var p in json.Take(15)) _output.WriteLine("  any: " + p);
    }

    /// <summary>Which packages reference the crop stage meshes (SM_*_Farmable_*), searched in raw package bytes.</summary>
    [Fact]
    public void Dump_WhoReferencesCropMeshes()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var candidates = assets.AssetPaths.Where(p => (p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase))
            && (p.Contains("/Blueprints/", StringComparison.OrdinalIgnoreCase) || p.Contains("/Data", StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var path in candidates)
        {
            var bytes = assets.ReadRawFile(path);
            if (bytes is null) continue;
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            var at = text.IndexOf("_Farmable_", StringComparison.Ordinal);
            if (at < 0) continue;
            var hits = System.Text.RegularExpressions.Regex.Matches(text, @"SM_[A-Za-z0-9]+_Farmable_[A-Za-z0-9]+").Select(m => m.Value).Distinct().Take(6);
            _output.WriteLine($"{path}: {string.Join(", ", hits)}");
        }
    }

    /// <summary>DT_Plants raw: size, its name map, and the bytes of its table export, to see why no rows parse (ABIOTIC_RAW_OUT gets the bytes).</summary>
    [Fact]
    public void Dump_PlantsTableRaw()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        const string path = "AbioticFactor/Content/Blueprints/DataTables/DT_Plants.uasset";
        var raw = assets.ReadRawFile(path);
        _output.WriteLine($"raw size {raw?.Length}");
        var outPath = Environment.GetEnvironmentVariable("ABIOTIC_RAW_OUT");
        if (outPath is not null && raw is not null) File.WriteAllBytes(outPath, raw);
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage(path);
            var names = pkg.NameMap.Select(n => n.Name).ToList();
            _output.WriteLine($"names ({names.Count}): {string.Join(" ", names)}");
            if (pkg is CUE4Parse.UE4.Assets.IoPackage io)
            {
                foreach (var e in io.ExportMap) _output.WriteLine($"export offset {e.CookedSerialOffset} size {e.CookedSerialSize} ");
            }
            return 0;
        });
    }

    /// <summary>DT_Plants read by the plugin's own reader: every row's fields.</summary>
    [Fact]
    public void Dump_PlantRows()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        var rows = assets.UseFileProvider(AbioticEditor.Plugins.GameModels3D.PlantTable.Read);
        _output.WriteLine($"{rows.Count} rows: {string.Join(", ", rows.Keys)}");
        foreach (var (name, row) in rows.Take(3).Concat(rows.Where(r => r.Key.Contains("Rope", StringComparison.OrdinalIgnoreCase) || r.Key.Contains("Rappel", StringComparison.OrdinalIgnoreCase))))
        {
            _output.WriteLine($"row {name}:");
            foreach (var prop in row.Properties) _output.WriteLine($"   {prop.Name.Text} = {Describe(prop.Tag?.GenericValue)}");
        }
    }

    [Fact]
    public void Debug_PlantRowsAtOffset()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            const string path = AbioticEditor.Plugins.GameModels3D.PlantTable.Path;
            var ok = p.TryLoadPackage(path, out var package);
            _output.WriteLine($"package {ok} {package?.GetType().Name}");
            var saved = p.TrySaveAsset(path, out var raw);
            _output.WriteLine($"saved {saved} {raw?.Length}");
            if (package is not CUE4Parse.UE4.Assets.IoPackage io || raw is null) return 0;
            var size = io.ExportMap[0].CookedSerialSize;
            var data = raw.AsSpan(raw.Length - (int)size).ToArray();
            _output.WriteLine($"first bytes {string.Join(" ", data.Take(24).Select(b => b.ToString("x2")))}");
            var ar = new CUE4Parse.UE4.Assets.Readers.FAssetArchive(new CUE4Parse.UE4.Readers.FByteArchive("x", data, p.Versions), io) { Position = 13 };
            var n = ar.Read<int>();
            _output.WriteLine($"count {n}");
            try
            {
                ar.Position = 27;
                var text = new CUE4Parse.UE4.Objects.Core.i18N.FText(ar);
                _output.WriteLine($"FText '{text.Text}' ends at {ar.Position} (expect 82)");
                ar.Position = 82;
                var soft = new CUE4Parse.UE4.Objects.UObject.FSoftObjectPath(ar);
                _output.WriteLine($"soft '{soft.AssetPathName.Text}' ends at {ar.Position} (expect 102); ver {ar.Ver} game {ar.Game}");
                foreach (var at in new[] { 102, 116, 130 })
                {
                    ar.Position = at;
                    try { var h = new FStructFallback(ar, "DataTableRowHandle"); _output.WriteLine($"handle at {at} ends {ar.Position}: {string.Join(",", h.Properties.Select(x => x.Name.Text + "=" + x.Tag?.GenericValue))}"); }
                    catch (Exception e) { _output.WriteLine($"handle at {at} failed: {e.Message}"); }
                }
                AbioticEditor.Plugins.GameModels3D.PlantTable.Read(p);
                var t = p.MappingsForGame!.Types["AbioticEditor_PlantData"];
                _output.WriteLine("corrected: " + string.Join(", ", t.Properties.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Name}/{kv.Value.Index}")) + " count " + t.PropertyCount);
                ar.Position = 17;
                for (var i = 0; i < n; i++)
                {
                    var name = ar.ReadFName();
                    var row = new FStructFallback(ar, "AbioticEditor_PlantData");
                    _output.WriteLine($"row {name.Text} parsed, pos {ar.Position}/{data.Length}, props {string.Join(",", row.Properties.Select(x => x.Name.Text))}");
                }
            }
            catch (Exception ex) { for (var e = ex; e is not null; e = e.InnerException) _output.WriteLine("failed: " + e.GetType().Name + " " + e.Message); _output.WriteLine(ex.ToString()[..Math.Min(3000, ex.ToString().Length)]); }
            return 0;
        });
    }

    [Fact]
    public void Dump_PlantStructMappings()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            foreach (var name in new[] { "PlantData", "PlantMeshData" })
            {
                if (p.MappingsForGame?.Types.TryGetValue(name, out var t) != true) { _output.WriteLine($"{name}: not mapped"); continue; }
                _output.WriteLine($"{name}: super {t!.SuperType} count {t.PropertyCount}");
                foreach (var (index, prop) in t.Properties.OrderBy(kv => kv.Key)) _output.WriteLine($"   [{index}] {prop.Name} : {prop.MappingType.Type} {prop.MappingType.StructType}{prop.MappingType.EnumName} {prop.MappingType.InnerType?.Type} {prop.MappingType.InnerType?.EnumName}{prop.MappingType.InnerType?.StructType} -> {prop.MappingType.ValueType?.Type} {prop.MappingType.ValueType?.StructType} size {prop.ArraySize}");
            }
            if (p.MappingsForGame?.Enums.TryGetValue("EPlantGrowthStage", out var e) == true) _output.WriteLine($"EPlantGrowthStage: {string.Join(", ", e.Select(kv => $"{kv.Key}={kv.Value}"))}");
            return 0;
        });
    }

    /// <summary>The planting spot's child actor class (from GardenPlot_Medium's Plot1) and its components.</summary>
    [Fact]
    public void Dump_SpotChildActor()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            var pkg = p.LoadPackage("AbioticFactor/Content/Blueprints/DeployedObjects/Farming/GardenPlot_Medium.uasset");
            var node = pkg.GetExports().OfType<USCS_Node>().First(n => n.InternalVariableName.Text == "Plot1");
            var template = node.ComponentTemplate!.Load()!;
            var childClass = template.GetOrDefault<FPackageIndex?>("ChildActorClass");
            _output.WriteLine($"child class {childClass?.ResolvedObject?.GetPathName()}");
            var cls = childClass?.Load<UStruct>();
            for (var c = cls; c is UBlueprintGeneratedClass bp; c = c.SuperStruct?.Load<UStruct>())
            {
                _output.WriteLine($"class {bp.Name}");
                var scs = bp.GetOrDefault<FPackageIndex?>("SimpleConstructionScript")?.Load<CUE4Parse.UE4.Assets.Exports.Engine.USimpleConstructionScript>();
                foreach (var n in scs?.AllNodes ?? [])
                {
                    var sn = n?.Load<USCS_Node>(); var t = sn?.ComponentTemplate?.Load();
                    _output.WriteLine($"   node {sn?.InternalVariableName.Text,-24} {t?.ExportType,-26} loc {t?.GetOrDefault("RelativeLocation", CUE4Parse.UE4.Objects.Core.Math.FVector.ZeroVector)} rot {t?.GetOrDefault("RelativeRotation", CUE4Parse.UE4.Objects.Core.Math.FRotator.ZeroRotator)} scale {t?.GetOrDefault("RelativeScale3D", CUE4Parse.UE4.Objects.Core.Math.FVector.OneVector)} mesh {t?.GetOrDefault<FPackageIndex?>("StaticMesh")?.ResolvedObject?.Name.Text} parent {sn?.GetOrDefault<FName>("ParentComponentOrVariableName").Text}");
                }
                var defaults = (bp as UClass)?.ClassDefaultObject.Load();
                foreach (var prop in defaults?.Properties.Where(x => !x.Name.Text.StartsWith("UberGraph", StringComparison.Ordinal)) ?? []) _output.WriteLine($"   cdo {prop.Name.Text} = {Describe(prop.Tag?.GenericValue)}");
            }
            return 0;
        });
    }

    /// <summary>The liquid container parent: its defaults and the bytecode of functions touching the fill (ABIOTIC_LIQUID_OUT gets the JSON).</summary>
    [Fact]
    public void Dump_LiquidContainerFill()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var outPath = Environment.GetEnvironmentVariable("ABIOTIC_LIQUID_OUT");
        if (assets is null || outPath is null) return;
        assets.UseFileProvider(p =>
        {
            var old = p.ReadScriptData;
            p.ReadScriptData = true;
            try
            {
                var pkg = p.LoadPackage(p.Files.Keys.First(k => k.EndsWith("/Deployed_LiquidContainer_ParentBP.uasset", StringComparison.OrdinalIgnoreCase)));
                using var w = new StreamWriter(outPath);
                var cls = pkg.GetExports().OfType<UBlueprintGeneratedClass>().First();
                var defaults = cls.ClassDefaultObject.Load();
                foreach (var prop in defaults?.Properties ?? []) w.WriteLine($"cdo {prop.Name.Text} = {Describe(prop.Tag?.GenericValue)}");
                foreach (var f in pkg.GetExports().OfType<UFunction>())
                {
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(f);
                    if (!json.Contains("Liquid_Fill", StringComparison.Ordinal) && !json.Contains("WaterLevel", StringComparison.Ordinal)) continue;
                    w.WriteLine("== " + f.Name);
                    w.WriteLine(json);
                }
            }
            finally { p.ReadScriptData = old; }
            return 0;
        });
    }

    /// <summary>The values of an enum from the mappings (ABIOTIC_ENUM).</summary>
    [Fact]
    public void Dump_Enum()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var name = Environment.GetEnvironmentVariable("ABIOTIC_ENUM") ?? "EDynamicProperty";
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            if (p.MappingsForGame?.Enums.TryGetValue(name, out var e) == true) _output.WriteLine($"{name}: {string.Join(", ", e.Select(kv => $"{kv.Key}={kv.Value}"))}");
            else _output.WriteLine(name + " not mapped");
            return 0;
        });
    }

    /// <summary>The ChangableData_ fields of liquid containers in a world save (ABIOTIC_SAVE).</summary>
    [Fact]
    public void Dump_LiquidContainerSaveFields()
    {
        var save = Environment.GetEnvironmentVariable("ABIOTIC_SAVE");
        if (save is null || !File.Exists(save)) return;
        var data = AbioticEditor.Core.WorldSaves.WorldSaveReader.ReadFromFile(save);
        foreach (var e in AbioticEditor.Core.WorldSaves.Features.WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            var cls = e.Props.FirstOrDefault(t => t.Name?.Value?.StartsWith("Class_", StringComparison.Ordinal) == true)?.Property?.Value?.ToString() ?? "";
            if (!cls.Contains("Liquid", StringComparison.Ordinal) && !cls.Contains("GardenPlot", StringComparison.Ordinal)) continue;
            var change = e.Props.FirstOrDefault(t => t.Name?.Value?.StartsWith("ChangableData", StringComparison.Ordinal) == true)?.Property?.Value as UeSaveGame.StructData.PropertiesStruct;
            _output.WriteLine($"{cls.Split('.').Last()}: {string.Join(", ", change?.Properties.Select(t => $"{t.Name?.Value}={t.Property?.Value}") ?? [])}");
        }
    }

    /// <summary>A user-defined enum asset's names, values and display names (ABIOTIC_ENUM_ASSET, a file name).</summary>
    [Fact]
    public void Dump_UserDefinedEnum()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        var name = Environment.GetEnvironmentVariable("ABIOTIC_ENUM_ASSET") ?? "E_LiquidType";
        if (assets is null) return;
        assets.UseFileProvider(p =>
        {
            var path = p.Files.Keys.First(k => k.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase));
            var e = p.LoadPackage(path).GetExports().OfType<CUE4Parse.UE4.Objects.UObject.UEnum>().First();
            _output.WriteLine($"{path}: {string.Join(", ", e.Names.Select(n => $"{n.Item1.Text}={n.Item2}"))}");
            var display = e.GetOrDefault<CUE4Parse.UE4.Assets.Objects.UScriptMap?>("DisplayNameMap");
            if (display is not null) _output.WriteLine("display: " + string.Join(", ", display.Properties.Select(kv => $"{kv.Key?.GenericValue}={kv.Value?.GenericValue}")));
            return 0;
        });
    }
}
