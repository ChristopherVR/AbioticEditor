using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research: which placed character trades as which trader. Dumps every TraderComponent in the level
/// files with its owner and properties. Set TRADER_PROBE_OUT to run.
/// </summary>
public sealed class TraderPlacementProbe
{
    [Fact]
    public void Dump_trader_components()
    {
        var output = Environment.GetEnvironmentVariable("TRADER_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(GameAssetProvider.FindConventionalMappings()!);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));

        using var writer = new StreamWriter(output, false);
        foreach (var map in provider.Files.Keys.Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p))
        {
            CUE4Parse.UE4.Assets.Exports.UObject[] exports;
            try { exports = provider.LoadPackage(map).GetExports().ToArray(); }
            catch { continue; }
            foreach (var e in exports.Where(e => e.Name.StartsWith("TraderComponent", StringComparison.OrdinalIgnoreCase)))
            {
                writer.WriteLine($"{Path.GetFileNameWithoutExtension(map)} | {e.Outer?.Name}.{e.Name}");
                foreach (var p in e.Properties) writer.WriteLine($"    {p.Name.Text} = {Describe(p.Tag?.GenericValue)}");
            }
        }
    }

    private static string Describe(object? v) => v switch
    {
        CUE4Parse.UE4.Assets.Objects.FScriptStruct ss => Describe(ss.StructType),
        CUE4Parse.UE4.Assets.Objects.FStructFallback s => "{" + string.Join(", ", s.Properties.Select(p => $"{p.Name.Text}={Describe(p.Tag?.GenericValue)}")) + "}",
        null => "null",
        _ => v.ToString() ?? "",
    };
}
