using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research dump for the creature picture catalog (<c>CreatureWikiImages</c>): every shipped NPC
/// blueprint with the NPC data-table row its defaults point at, plus every data table whose name
/// mentions NPCs (the row's display name is what the wiki titles its page after). Set
/// <c>NPC_ROSTER_PROBE_OUT</c> to a directory to run it.
/// </summary>
public sealed class NpcRosterProbe
{
    [Fact]
    public void Dump_npc_classes_with_their_data_rows()
    {
        var output = Environment.GetEnvironmentVariable("NPC_ROSTER_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(GameAssetProvider.FindConventionalMappings()!);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        Directory.CreateDirectory(output);

        var lines = new List<string>();
        foreach (var path in provider.Files.Keys.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && p.Contains("/Blueprints/Characters/NPCs/", StringComparison.OrdinalIgnoreCase)
                     && Path.GetFileNameWithoutExtension(p).StartsWith("NPC", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().ToArray();
                var cdo = exports.FirstOrDefault(e => e.Name.StartsWith("Default__", StringComparison.Ordinal));
                var row = cdo?.Properties.FirstOrDefault(p => p.Name.Text == "NPCDataTableRow")?.Tag?.GenericValue?.ToString();
                var super = (exports.FirstOrDefault(e => e.ExportType.EndsWith("GeneratedClass", StringComparison.Ordinal)) as CUE4Parse.UE4.Objects.UObject.UStruct)?.SuperStruct?.Name;
                lines.Add($"{Path.GetFileNameWithoutExtension(path)}\t{row}\t{super}");
            }
            catch (Exception ex)
            {
                lines.Add($"{Path.GetFileNameWithoutExtension(path)}\tERROR\t{ex.GetType().Name}");
            }
        }
        File.WriteAllLines(Path.Combine(output, "npc-rows.tsv"), lines);

        foreach (var path in provider.Files.Keys.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && Path.GetFileNameWithoutExtension(p).Contains("NPC", StringComparison.OrdinalIgnoreCase)
                     && (p.Contains("/Data/", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith("DT_", StringComparison.OrdinalIgnoreCase))))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().OfType<UDataTable>().ToArray();
                if (exports.Length == 0) continue;
                var rows = exports[0].RowMap.ToDictionary(
                    kvp => kvp.Key.Text,
                    kvp => kvp.Value.Properties.Where(p => p.Tag is not null && (p.Name.Text.Contains("Name", StringComparison.OrdinalIgnoreCase) || p.Name.Text.Contains("Compendium", StringComparison.OrdinalIgnoreCase) || p.Name.Text.Contains("Class", StringComparison.OrdinalIgnoreCase)))
                        .ToDictionary(p => p.Name.Text, p => p.Tag!.GenericValue?.ToString()));
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(rows, Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".error.txt"), ex.ToString());
            }
        }
    }
}
