using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 91: what a dropped item's InitDespawn/OnItemDespawn and pickup path actually do to the
/// actor, so the live GROUND ITEMS list can tell a removed item apart from a live one before the
/// engine's garbage collector finally frees it. Env-var gated like every other probe.
/// </summary>
public sealed class DroppedItemDespawnProbe
{
    [Fact]
    public void Dump_dropped_item_exports()
    {
        var output = Environment.GetEnvironmentVariable("DROPPED_ITEM_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(GameAssetProvider.FindConventionalMappings()!);
        provider.ReadScriptData = true;
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        Directory.CreateDirectory(output);

        string[] names = ["Abiotic_Item_Dropped"];
        foreach (var path in provider.Files.Keys.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && names.Contains(Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().ToArray();
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".error.txt"), ex.ToString());
            }
        }
    }
}
