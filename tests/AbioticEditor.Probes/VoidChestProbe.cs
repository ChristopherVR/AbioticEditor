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
            "AbioticDeployed_ParentBP", "Abiotic_HUD_Widget_RenameObject", "Abiotic_PlayerController", "Abiotic_Character",
            "Deployed_StorageCrate_Void", "Inventory_Void", "Abiotic_Survival_GameState", "W_Container", "W_ContainerHealthBar",
            "Abiotic_InventoryComponent"];
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

        // Round 84: every uasset whose bare name suggests a Void Chest variant, or a container
        // health/durability widget, so we can check each variant's real parent class and each
        // widget's real data binding instead of assuming only one Void Chest blueprint exists or
        // that "health" means CurrentDurability/MaxDurability.
        var voidCandidates = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(p).Contains("Void", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        File.WriteAllLines(Path.Combine(output, "_void_candidates.txt"), voidCandidates);

        // Round 85: broader than "Void" - every Blueprint (not material/texture) whose name
        // suggests any kind of chest, in case the specific damaged one the player is standing at
        // is a different class entirely (a boss-reward chest, say) rather than the ordinary
        // Deployed_StorageCrate_Void_C this file already confirmed is the only "Void"-named one.
        var chestCandidates = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && p.Contains("Blueprint", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(p).Contains("Chest", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        File.WriteAllLines(Path.Combine(output, "_chest_blueprint_candidates.txt"), chestCandidates);

        var healthWidgetCandidates = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && (Path.GetFileNameWithoutExtension(p).Contains("Durabil", StringComparison.OrdinalIgnoreCase)
                    || (Path.GetFileNameWithoutExtension(p).Contains("Container", StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(p).Contains("Health", StringComparison.OrdinalIgnoreCase))
                    || (Path.GetFileNameWithoutExtension(p).StartsWith("W_", StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(p).Contains("Container", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        File.WriteAllLines(Path.Combine(output, "_health_widget_candidates.txt"), healthWidgetCandidates);

        // Dump every distinct Void Chest blueprint class found above (up to a sane cap) so we can
        // check each one's actual parent chain and GetContainerInventory override.
        foreach (var path in voidCandidates.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)).Take(20))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().ToArray();
                var safeName = Path.GetFileNameWithoutExtension(path).Replace('/', '_').Replace('\\', '_');
                File.WriteAllText(Path.Combine(output, "void_" + safeName + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "void_" + Path.GetFileNameWithoutExtension(path) + ".error.txt"), ex.ToString());
            }
        }
    }
}
