using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Research dump: tram and station actors in the Facility level, with their properties.</summary>
public class TramLevelProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void Dump_TramAndStationActors()
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

        foreach (var f in provider.Files.Keys.Where(p => p.Contains("Tram", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && p.StartsWith("AbioticFactor/Content/Blueprints", StringComparison.OrdinalIgnoreCase)).Take(60))
            output.WriteLine("ASSET " + f);

        var levels = provider.Files.Keys.Where(p => p.StartsWith("AbioticFactor/Content/Maps/Facility", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var l in levels) output.WriteLine("LEVEL " + l);

        foreach (var level in levels)
        {
            try
            {
                var pkg = provider.LoadPackage(level);
                var n = 0;
                foreach (var export in pkg.GetExports())
                {
                    var cls = export.Class?.Name.Text ?? "?";
                    if (!cls.Contains("Tram", StringComparison.OrdinalIgnoreCase) && !export.Name.Contains("Tram", StringComparison.OrdinalIgnoreCase)) continue;
                    n++;
                    output.WriteLine($"EXPORT {Path.GetFileName(level)} {cls} {export.Name}");
                    foreach (var p in export.Properties.Take(40))
                        output.WriteLine($"    {p.Name.Text} = {Short(p.Tag?.GenericValue)}");
                }
                output.WriteLine($"TOTAL {level}: {n}");
            }
            catch (Exception e) { output.WriteLine($"!! {level}: {e.Message}"); }
        }
    }

    private static string Short(object? v)
    {
        var s = v?.ToString() ?? "null";
        return s.Length > 160 ? s[..160] + "..." : s;
    }
}
