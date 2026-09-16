using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

public sealed class LiveWorldCareProbe
{
    [Fact]
    public void Dump_world_care_and_clock_functions()
    {
        var output = Environment.GetEnvironmentVariable("LIVE_WORLD_CARE_PROBE_OUT");
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
        string[] names = ["GardenPlot_ParentBP", "FarmingPlot_BP", "Deployed_Furniture_Chair_PowerChair", "RechargeableComponent",
            "Deployed_ProcessingBench_ParentBP", "Deployed_ChemistryBench", "AbioticDeployed_CraftingBench_ParentBP",
            "Abiotic_Survival_GameState", "Abiotic_Survival_GameMode", "Abiotic_GameInstance", "Abiotic_WorldMetadataSave", "DayNightManager"];
        foreach (var path in provider.Files.Keys.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && names.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            var exports = provider.LoadPackage(path).GetExports().ToArray();
            File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
        }
    }
}
