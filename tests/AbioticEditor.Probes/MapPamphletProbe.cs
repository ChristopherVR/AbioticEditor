using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probes the game's map pamphlet data: which textures the in-game sector maps use and
/// whether any asset carries world-bounds metadata that would let the editor project
/// world coordinates onto the map images.
/// </summary>
public class MapPamphletProbe
{
    private readonly ITestOutputHelper _output;

    public MapPamphletProbe(ITestOutputHelper output)
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
    public void Dump_MapPamphlets_Columns()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no install"); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Environment/DT_MapPamphlets");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    Dump(p.Name.Text, p.Tag?.GenericValue, 1);
                }
            }
        }
    }

    private void Dump(string name, object? value, int depth)
    {
        var indent = new string(' ', depth * 2);
        if (value is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) value = ss.StructType;
        if (value is CUE4Parse.UE4.Assets.Objects.FStructFallback sf)
        {
            _output.WriteLine($"{indent}{name}:");
            foreach (var p in sf.Properties)
            {
                Dump(p.Name.Text, p.Tag?.GenericValue, depth + 1);
            }
            return;
        }
        var s = value?.ToString() ?? "(null)";
        if (s.Length > 220) s = s[..220];
        _output.WriteLine($"{indent}{name} = {s}");
    }

    [Fact]
    public void Dump_DTLevels_Columns()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no install"); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Environment/DT_Levels");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            _output.WriteLine($"ROWS: {dt.RowMap.Count}");
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    Dump(p.Name.Text, p.Tag?.GenericValue, 1);
                }
            }
        }
    }

    [Fact]
    public void Find_MapRelated_Assets()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("no install"); return; }

        foreach (var path in provider.Files.Keys)
        {
            var lower = path.ToLowerInvariant();
            if ((lower.Contains("pamphlet") || lower.Contains("worldmap") || lower.Contains("minimap")
                 || (lower.Contains("map") && lower.Contains("texture")))
                && !lower.StartsWith("abioticfactor/content/maps/"))
            {
                _output.WriteLine(path);
            }
        }
    }
}
