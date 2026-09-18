using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research dump for the NPCS tab's story characters: the save's NarrativeNPCMap keys are
/// anonymous placed actors (PersistentLevel.NarrativeNPC_Human_ParentBP_C_1), so this dumps the
/// placed NarrativeNPC_* actor exports of every level package, with their instance properties,
/// to see whether the level itself says which character each placed actor is.
/// Set <c>NARRATIVE_NPC_LEVEL_PROBE_OUT</c> to a directory to run it.
/// </summary>
public sealed class NarrativeNpcLevelProbe
{
    [Fact]
    public void Dump_placed_narrative_npc_actors()
    {
        var output = Environment.GetEnvironmentVariable("NARRATIVE_NPC_LEVEL_PROBE_OUT");
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

        var maps = provider.Files.Keys
            .Where(p => p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllLines(Path.Combine(output, "maps.txt"), maps);

        using var summary = new StreamWriter(Path.Combine(output, "narrative-actors.txt"), false);
        foreach (var map in maps)
        {
            UObject[] exports;
            try { exports = provider.LoadPackage(map).GetExports().ToArray(); }
            catch (Exception ex) { summary.WriteLine($"--- {map}: load failed ({ex.GetType().Name}: {ex.Message})"); continue; }

            var actors = exports.Where(e => e.Name.StartsWith("NarrativeNPC_", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (actors.Length == 0) continue;
            summary.WriteLine($"--- {map}");
            foreach (var actor in actors)
            {
                summary.WriteLine($"  {actor.Name} ({actor.Class?.Name})");
                foreach (var prop in actor.Properties)
                    summary.WriteLine($"    {prop.Name.Text} = {prop.Tag?.GenericValue}");
            }
            File.WriteAllText(
                Path.Combine(output, Path.GetFileNameWithoutExtension(map) + ".json"),
                JsonConvert.SerializeObject(actors, Formatting.Indented));
        }
    }
}
