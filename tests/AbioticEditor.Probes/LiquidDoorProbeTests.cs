using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class LiquidDoorProbeTests
{
    private readonly ITestOutputHelper _output;
    public LiquidDoorProbeTests(ITestOutputHelper output) { _output = output; }

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    [Fact]
    public void Dump_LiquidEnum()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var path = provider.Files.Keys.FirstOrDefault(p => p.Contains("E_LiquidType", StringComparison.OrdinalIgnoreCase));
        _output.WriteLine($"asset: {path}");
        if (path is null) return;

        var pkg = provider.LoadPackage(path.Replace(".uasset", ""));
        foreach (var export in pkg.GetExports())
        {
            if (export is not UEnum en) continue;
            foreach (var (name, value) in en.Names)
            {
                _output.WriteLine($"ENUM {name.Text} = {value}");
            }
            // DisplayNameMap (friendly names) is a tagged property on UserDefinedEnum.
            foreach (var p in export.Properties)
            {
                if (!p.Name.Text.Contains("DisplayNameMap")) continue;
                if (p.Tag?.GenericValue is CUE4Parse.UE4.Assets.Objects.UScriptMap map)
                {
                    foreach (var kv in map.Properties)
                    {
                        _output.WriteLine($"DISPLAY {kv.Key?.GenericValue} => {kv.Value?.GenericValue}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Dump_LiquidColumns_ForContainers()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            string[] ids = { "waterbottle", "canteen", "pot", "soup_bowl", "personalteleporter", "bandage" };
            foreach (var kv in dt.RowMap)
            {
                var id = kv.Key.Text;
                if (!ids.Any(x => id.Equals(x, StringComparison.OrdinalIgnoreCase))
                    && !id.Contains("waterbottle", StringComparison.OrdinalIgnoreCase)
                    && !id.Contains("canteen", StringComparison.OrdinalIgnoreCase)) continue;

                _output.WriteLine($"=== {id} ===");
                foreach (var p in kv.Value.Properties)
                {
                    if (!p.Name.Text.Contains("Liquid", StringComparison.OrdinalIgnoreCase)) continue;
                    DumpDeep(p.Name.Text, p.Tag?.GenericValue, 1);
                }
            }
        }
    }

    private void DumpDeep(string name, object? value, int depth)
    {
        var pad = new string(' ', depth * 2);
        switch (value)
        {
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                DumpDeep(name, ss.StructType, depth);
                break;
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                _output.WriteLine($"{pad}{name}:");
                foreach (var p in sf.Properties) DumpDeep(p.Name.Text, p.Tag?.GenericValue, depth + 1);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptArray arr:
                _output.WriteLine($"{pad}{name}[{arr.Properties.Count}]:");
                var i = 0;
                foreach (var el in arr.Properties) DumpDeep($"[{i++}]", el.GenericValue, depth + 1);
                break;
            default:
                var s = value?.ToString() ?? "(null)";
                if (s.Length > 160) s = s[..160];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }

    [Fact]
    public void Survey_DoorTextures()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        foreach (var p in provider.Files.Keys.Where(p =>
            p.Contains("Door", StringComparison.OrdinalIgnoreCase)
            && (p.Contains("/GUI/", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Icon", StringComparison.OrdinalIgnoreCase))))
        {
            _output.WriteLine(p);
        }
    }
}
