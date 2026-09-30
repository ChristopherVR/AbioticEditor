using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Research dump: the game's buff/debuff data tables (row names, fields, durations).</summary>
public class BuffTableProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void Dump_BuffTables()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) { output.WriteLine("no install"); return; }
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var candidates = provider.Files.Keys
            .Where(p => p.StartsWith("AbioticFactor/", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && (p.Contains("Buff", StringComparison.OrdinalIgnoreCase) || p.Contains("Debuff", StringComparison.OrdinalIgnoreCase)))
            .Take(80).ToList();
        foreach (var c in candidates) output.WriteLine("ASSET " + c);

        foreach (var path in candidates.Where(p => p.Contains("/DT_", StringComparison.OrdinalIgnoreCase) || p.Contains("DataTable", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var pkg = provider.LoadPackage(path);
                foreach (var export in pkg.GetExports().OfType<UDataTable>())
                {
                    output.WriteLine($"== {path} rows={export.RowMap.Count} struct={export.RowStructName}");
                    foreach (var kv in export.RowMap.Take(400))
                    {
                        output.WriteLine("ROW " + kv.Key.Text + " :: " + string.Join("; ", kv.Value.Properties.Select(p => $"{p.Name.Text}={Short(p.Tag?.GenericValue)}")));
                    }
                }
            }
            catch (Exception e) { output.WriteLine($"!! {path}: {e.Message}"); }
        }
    }

    private static string Short(object? v)
    {
        var s = v?.ToString() ?? "null";
        return s.Length > 90 ? s[..90] + "..." : s;
    }
}
