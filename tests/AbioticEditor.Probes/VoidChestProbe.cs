using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

public sealed class VoidChestProbe
{
    [Fact]
    public void Find_void_chest_paths_and_dump_container_exports()
    {
        var output = Environment.GetEnvironmentVariable("VOID_CHEST_PROBE_OUT");
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

        string[] names = ["AbioticDeployed_Furniture_ParentBP", "Deployed_Container_ParentBP", "Deployed_StorageCrate_ParentBP",
            "AbioticDeployed_ParentBP", "Abiotic_HUD_Widget_RenameObject", "Abiotic_PlayerController", "Abiotic_Character"];
        foreach (var path in provider.Files.Keys.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && names.Contains(Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().ToArray();
                File.WriteAllText(Path.Combine(output, "furn_" + Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "furn_" + Path.GetFileNameWithoutExtension(path) + ".error.txt"), ex.ToString());
            }
        }

        // Also list every uasset whose bare name contains "Rename" or "ObjectName" - the actual
        // rename widget/RPC might not be named anything in this guessed list above.
        var candidates = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && (Path.GetFileNameWithoutExtension(p).Contains("Rename", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(p).Contains("ObjectName", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        File.WriteAllLines(Path.Combine(output, "_rename_candidates.txt"), candidates);
    }
}
