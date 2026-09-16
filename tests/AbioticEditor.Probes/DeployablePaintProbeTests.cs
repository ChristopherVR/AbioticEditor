using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
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
/// Research probe for placed-object (deployable) paint colour - see
/// <c>docs/reference/research/research-deployable-paint.md</c> for the write-up and
/// <see cref="DeployablePaintCatalog"/> for the resulting Core catalog. Grounds, from the pak and
/// from real saves:
/// <list type="bullet">
///   <item><c>DT_PaintedDeployables</c>'s 38 rows and 13 colour columns.</item>
///   <item><c>AbioticDeployed_ParentBP_C</c>'s <c>PaintedColor</c> (plain <c>EPaintColor</c>
///     property, no hash suffix) and <c>SetPaintColor</c>/<c>OnRep_PaintedColor</c>.</item>
///   <item>The <c>EPaintColor</c> and <c>EDynamicProperty</c> enums.</item>
///   <item>That the actual save struct (<c>SaveData_Deployable_Struct</c>) carries no paint
///     field at all - painting rides the same <c>ChangableData_.DynamicProperties_</c> array
///     item slots already use for weapon coatings, keyed <c>EDynamicProperty::PaintColor</c>.</item>
///   <item>The class -&gt; <c>DT_PaintedDeployables</c> row map, from every blueprint CDO's own
///     default <c>PaintedDeployableRow</c>.</item>
///   <item>Real, already-painted deployables in the Cascade fixture world, confirming the exact
///     on-disk shape at the byte level.</item>
/// </list>
/// Output-only; every fact here is also asserted into <see cref="DeployablePaintCatalog"/> or the
/// reader/writer, so this class does not assert anything itself.
/// </summary>
public class DeployablePaintProbeTests
{
    private readonly ITestOutputHelper _output;
    public DeployablePaintProbeTests(ITestOutputHelper output) => _output = output;

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    /// <summary>The <c>EPaintColor</c> and <c>EDynamicProperty</c> enums, straight from the usmap.</summary>
    [Fact]
    public void Dump_PaintEnums()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no paks/mappings; skipping"); return; }

        foreach (var enumName in new[] { "EPaintColor", "EDynamicProperty" })
        {
            if (!provider.MappingsForGame!.Enums.TryGetValue(enumName, out var values))
            {
                _output.WriteLine($"{enumName}: not found");
                continue;
            }
            _output.WriteLine($"ENUM {enumName} ({values.Count} values):");
            foreach (var (value, name) in values.OrderBy(v => v.Key))
                _output.WriteLine($"    [{value}] {name}");
        }
    }

    /// <summary>
    /// The deployable class layout: PaintedColor/PaintedDeployableRow plus the paint functions,
    /// and the actual per-instance save struct (which carries no paint field of its own).
    /// </summary>
    [Fact]
    public void Dump_DeployableClassAndSaveStruct()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no paks/mappings; skipping"); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DeployedObjects/AbioticDeployed_ParentBP");
        foreach (var export in pkg.GetExports())
        {
            if (export is UStruct st && export.Name == "AbioticDeployed_ParentBP_C")
            {
                _output.WriteLine($"CLASS {export.Name}: {st.ChildProperties?.Length ?? 0} properties");
                foreach (var c in st.ChildProperties ?? [])
                    if (c.Name.Text.Contains("Paint", StringComparison.OrdinalIgnoreCase))
                        _output.WriteLine($"    prop {c.Name.Text} : {c.GetType().Name}");
            }
            if (export is UFunction fn && fn.Name is "SetPaintColor" or "OnRep_PaintedColor" or "CanBePainted")
            {
                _output.WriteLine($"FUNC {fn.Name} flags={fn.FunctionFlags}");
                foreach (var p in fn.ChildProperties ?? [])
                    _output.WriteLine($"    param {p.Name.Text} : {p.GetType().Name}");
            }
        }

        var savePkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Saves/SaveData/SaveData_Deployable_Struct");
        foreach (var export in savePkg.GetExports())
        {
            if (export is not UStruct st) continue;
            _output.WriteLine($"SAVE STRUCT {export.Name} ({export.ExportType}):");
            foreach (var c in st.ChildProperties ?? [])
                _output.WriteLine($"    {c.Name.Text} : {c.GetType().Name}");
        }

        var changeablePkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Data/Abiotic_InventoryChangeableDataStruct");
        foreach (var export in changeablePkg.GetExports())
        {
            if (export is not UStruct st) continue;
            _output.WriteLine($"CHANGEABLE STRUCT {export.Name} ({export.ExportType}) (shared by item slots and deployables' ChangableData_):");
            foreach (var c in st.ChildProperties ?? [])
                _output.WriteLine($"    {c.Name.Text} : {c.GetType().Name}");
        }
    }

    /// <summary>DT_PaintedDeployables: 38 rows x Default + 13 colour columns.</summary>
    [Fact]
    public void Dump_PaintedDeployablesTable()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no paks/mappings; skipping"); return; }

        var table = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_PaintedDeployables")
            .GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Engine.UDataTable>().First();
        _output.WriteLine($"DT_PaintedDeployables: {table.RowMap.Count} rows, struct={table.RowStructName}");
        foreach (var key in table.RowMap.Keys.OrderBy(k => k.Text, StringComparer.OrdinalIgnoreCase))
            _output.WriteLine("  ROW " + key.Text);
    }

    /// <summary>
    /// Class -&gt; DT_PaintedDeployables row: every <c>Deployed_*</c> blueprint whose own CDO sets
    /// a non-None <c>PaintedDeployableRow</c> default. This is exactly
    /// <see cref="DeployablePaintCatalog"/>'s class map; kept here so the two can be diffed after
    /// a game update.
    /// </summary>
    [Fact]
    public void Dump_PaintableClassRowMap()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no paks/mappings; skipping"); return; }

        var deployedKeys = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(k => k.Contains("/Blueprints/DeployedObjects/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var found = 0;
        foreach (var key in deployedKeys)
        {
            CUE4Parse.UE4.Assets.IPackage pkg;
            try { pkg = provider.LoadPackage(key[..^".uasset".Length]); }
            catch { continue; }
            foreach (var export in pkg.GetExports())
            {
                if (!export.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                var rowProp = export.Properties.FirstOrDefault(p => p.Name.Text == "PaintedDeployableRow");
                if (rowProp?.Tag?.GenericValue is not { } val) continue;
                var row = Describe(val);
                if (row.StartsWith("RowName=None", StringComparison.Ordinal)) continue;
                found++;
                _output.WriteLine($"{export.Name} -> {row}");
            }
        }
        _output.WriteLine($"confirmed paintable classes: {found}");
    }

    private static string Describe(object? value)
    {
        switch (value)
        {
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                return Describe(ss.StructType);
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                return string.Join("; ", sf.Properties.Select(p => $"{p.Name.Text}={Describe(p.Tag?.GenericValue)}"));
            default:
                var s = value?.ToString() ?? "(null)";
                return s.Length > 120 ? s[..120] : s;
        }
    }

    /// <summary>
    /// Real-save confirmation: scans every <c>WorldSave_*.sav</c> fixture's <c>DeployedObjectMap</c>
    /// for an <c>EDynamicProperty::PaintColor</c> entry inside <c>ChangableData_.DynamicProperties_</c>,
    /// printing the owning class and the raw value. This is the byte-level evidence (not just game
    /// data) that painting rides the dynamic-properties array.
    /// </summary>
    [Fact]
    public void Scan_Fixtures_ForPaintedDeployables()
    {
        var roots = new List<string>();
        if (Fixtures.CascadeDir is { } cascade) roots.Add(cascade);
        if (Fixtures.ClientSavedDir is { } client) roots.Add(client);
        if (Fixtures.ServerWorldsDir is { } server) roots.Add(server);
        if (roots.Count == 0) { _output.WriteLine("no fixtures; skipping"); return; }

        var total = 0;
        foreach (var root in roots)
        {
            foreach (var path in Directory.EnumerateFiles(root, "WorldSave_*.sav", SearchOption.AllDirectories))
            {
                SaveGame save;
                try
                {
                    using var fs = File.OpenRead(path);
                    save = SaveGame.LoadFrom(fs);
                }
                catch { continue; }
                var tag = save.Properties?.FirstOrDefault(t => t.Name!.Value.StartsWith("DeployedObjectMap", StringComparison.Ordinal));
                if (tag?.Property is not MapProperty map || map.Value is null) continue;

                foreach (var kvp in map.Value)
                {
                    if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                    var cd = ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("ChangableData_", StringComparison.Ordinal));
                    if (cd?.Property is not StructProperty cdSp || cdSp.Value is not PropertiesStruct cdPs) continue;
                    var dyn = cdPs.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("DynamicProperties_", StringComparison.Ordinal));
                    if (dyn?.Property is not ArrayProperty { Value: { } arr }) continue;

                    foreach (StructProperty el in arr)
                    {
                        if (el.Value is not PropertiesStruct eps) continue;
                        var key = eps.Properties.FirstOrDefault(p => p.Name!.Value == "Key")?.Property?.Value?.ToString();
                        if (key?.EndsWith("::" + DeployablePaintCatalog.DynamicPropertyKey, StringComparison.Ordinal) != true) continue;
                        var value = eps.Properties.FirstOrDefault(p => p.Name!.Value == "Value")?.Property?.Value;
                        var className = ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("Class_", StringComparison.Ordinal))?.Property?.Value?.ToString();
                        total++;
                        _output.WriteLine($"{Path.GetFileName(path)} :: class={className} value={value}");
                    }
                }
            }
        }
        _output.WriteLine($"total painted deployables found across fixtures: {total}");
    }
}
