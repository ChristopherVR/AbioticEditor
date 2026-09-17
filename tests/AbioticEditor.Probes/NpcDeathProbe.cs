using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research dump for the live CREATURES kill/revive path: what the NPC base class does on death
/// and which AI/controller pieces a revive has to restore. Set <c>NPC_DEATH_PROBE_OUT</c> to a
/// directory to run it; it also writes <c>npc-classes.txt</c>, every NPC asset the game ships.
/// </summary>
public sealed class NpcDeathProbe
{
    [Fact]
    public void Dump_npc_base_and_robot_classes()
    {
        var output = Environment.GetEnvironmentVariable("NPC_DEATH_PROBE_OUT");
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

        var npcAssets = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && (p.Contains("/NPC", StringComparison.OrdinalIgnoreCase) || p.Contains("Robot", StringComparison.OrdinalIgnoreCase)
                    || p.Contains("Security", StringComparison.OrdinalIgnoreCase) || p.Contains("AIController", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllLines(Path.Combine(output, "npc-classes.txt"), npcAssets);

        string[] names = ["NPC_Base_ParentBP", "NPC_Robot_ParentBP", "NPC_Robot_Defense", "Abiotic_Character_ParentBP",
            "Abiotic_AI_Controller_ParentBP", "AI_Controller_NPC_Robot_Defense"];
        foreach (var path in provider.Files.Keys.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && names.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)))
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
