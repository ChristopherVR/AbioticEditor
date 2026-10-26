using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>Read-only evidence for the 1.5 game-data and save-schema migration.</summary>
public sealed class CommunityUpdateFiveProbe
{
    [Fact]
    public void Dump_update_assets()
    {
        var output = Environment.GetEnvironmentVariable("ABIOTIC_UPDATE_PROBE_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var saveDirectory = Environment.GetEnvironmentVariable("ABIOTIC_UPDATE_PROBE_SAVES");
        if (Directory.Exists(saveDirectory))
        {
            AbioticEditor.Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
            foreach (var file in Directory.EnumerateFiles(saveDirectory, "*.sav", SearchOption.AllDirectories))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    using var target = File.Create(Path.Combine(output, Path.GetFileName(file) + ".json"));
                    new UeSaveGame.Json.SaveGameSerializer().ConvertToJson(stream, target);
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output, Path.GetFileName(file) + ".error.txt"), ex.ToString()); }
            }
        }
        var modDirectory = Environment.GetEnvironmentVariable("ABIOTIC_UPDATE_PROBE_MODS");
        var modFiles = Directory.Exists(modDirectory) ? Directory.EnumerateFiles(modDirectory, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".pak" or ".utoc").ToArray() : [];
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: modFiles.Length > 0,
            selectedMods: [new("inspection", modFiles)]);
        Assert.NotNull(provider);
        provider.UseFileProvider(p => { p.ReadScriptData = true; return true; });
        File.WriteAllText(Path.Combine(output, "spawn-creatures.json"), JsonConvert.SerializeObject(
            AbioticEditor.Core.WorldSaves.NpcSpawnCatalog.LoadFrom(provider), Formatting.Indented));
        File.WriteAllText(Path.Combine(output, "catalogs.json"), JsonConvert.SerializeObject(new
        {
            Items = AbioticEditor.Core.Items.ItemCatalog.LoadFrom(provider).Entries,
            Recipes = AbioticEditor.Core.Items.RecipeCatalog.LoadInfosFrom(provider),
            Learning = AbioticEditor.Core.Items.RecipeLearningCatalog.LoadFrom(provider),
            Pets = AbioticEditor.Core.WorldSaves.PetGameData.TryLoadFrom(provider)?.Definitions,
            Milestones = AbioticEditor.Core.PlayerSaves.SkillMilestoneCatalog.LoadFrom(provider),
        }, Formatting.Indented));
        var paths = provider.AssetPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllLines(Path.Combine(output, "assets.txt"), paths);
        var requested = Environment.GetEnvironmentVariable("ABIOTIC_UPDATE_PROBE_ASSETS");
        var selected = string.IsNullOrEmpty(requested)
            ? paths.Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && (p.Contains("Quest", StringComparison.OrdinalIgnoreCase)
                    || p.Contains("ModRegistry", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith("/DT_Pets.uasset", StringComparison.OrdinalIgnoreCase)))
            : requested.Split(';');
        provider.UseFileProvider(p =>
        {
            File.WriteAllBytes(Path.Combine(output, "DefaultGame.ini"), p.SaveAsset("AbioticFactor/Config/DefaultGame.ini"));
            var mappings = p.MappingsContainer?.MappingsForGame;
            if (mappings is not null)
                File.WriteAllText(Path.Combine(output, "enums.json"), JsonConvert.SerializeObject(mappings.Enums, Formatting.Indented));
            if (mappings is not null)
                File.WriteAllText(Path.Combine(output, "schema.json"), JsonConvert.SerializeObject(
                    mappings.Types.Values.Where(t => t.Name.Contains("Abiotic", StringComparison.OrdinalIgnoreCase)
                        || t.Name.Contains("Quest", StringComparison.OrdinalIgnoreCase)
                        || t.Name.Contains("Mod", StringComparison.OrdinalIgnoreCase)
                        || t.Name.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
                        || t.Name.Contains("Pet", StringComparison.OrdinalIgnoreCase))
                        .Select(t => new { t.Name, t.SuperType, t.Properties }), Formatting.Indented));
            foreach (var path in selected)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var exports = p.LoadPackage(path).GetExports().ToArray();
                    File.WriteAllText(Path.Combine(output, name + ".json"),
                        JsonConvert.SerializeObject(exports, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, name + ".error.txt"), ex.ToString());
                }
            }
            return true;
        });
    }
}
