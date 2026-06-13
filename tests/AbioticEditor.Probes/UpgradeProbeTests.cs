using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class UpgradeProbeTests
{
    private readonly ITestOutputHelper _output;

    public UpgradeProbeTests(ITestOutputHelper output)
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

    [Fact]
    public void Dump_ItemUpgradesTable()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_ItemUpgrades");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            _output.WriteLine($"ROWS {dt.RowMap.Count}");
            foreach (var kv in dt.RowMap.Take(4))
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
                }
            }
            _output.WriteLine("KEYS " + string.Join(", ", dt.RowMap.Select(kv => kv.Key.Text)));
        }
    }

    private void DumpValue(string name, object? value, int depth)
    {
        var pad = new string(' ', depth * 2);
        switch (value)
        {
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                DumpValue(name, ss.StructType, depth);
                break;
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                _output.WriteLine($"{pad}{name}:");
                foreach (var p in sf.Properties) DumpValue(p.Name.Text, p.Tag?.GenericValue, depth + 1);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptArray arr:
                _output.WriteLine($"{pad}{name}[{arr.Properties.Count}]:");
                var i = 0;
                foreach (var el in arr.Properties) DumpValue($"[{i++}]", el.GenericValue, depth + 1);
                break;
            default:
                var s = value?.ToString() ?? "(null)";
                if (s.Length > 160) s = s[..160];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }

    [Fact]
    public void Dump_BenchCustomNames()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;

        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        var tag = save.Properties!.First(t => t.Name!.Value.StartsWith("DeployedObjectMap"));
        var map = (MapProperty)tag.Property!;

        foreach (var kvp in map.Value!)
        {
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
            var cls = ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("Class_"))?.Property?.Value?.ToString();
            if (cls?.Contains("CraftingBench", StringComparison.OrdinalIgnoreCase) != true) continue;

            _output.WriteLine($"BENCH {kvp.Key.Value}");
            var custom = ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("CustomTextDisplay_"))?.Property?.Value?.ToString();
            _output.WriteLine($"  CUSTOMTEXT='{custom}'");
            if (ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("ChangableData_"))?.Property is StructProperty cSp
                && cSp.Value is PropertiesStruct cPs)
            {
                foreach (var p in cPs.Properties)
                {
                    var v = p.Property?.Value?.ToString();
                    if (v?.Length > 90) v = v[..90];
                    _output.WriteLine($"  CHANGEABLE {p.Name!.Value} = {v}");
                }
            }
        }
    }
}
