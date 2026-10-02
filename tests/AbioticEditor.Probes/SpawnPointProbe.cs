using System.IO;
using AbioticEditor.Core.Assets;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research: what an NPC spawn point blueprint (<c>NPCSpawn_*_C</c>) says it spawns, so the NPC
/// Spawns list and the 3D view can show the creature's own picture instead of guessing it from the
/// spawn point's name. Set <c>SPAWN_PROBE_OUT</c> to a file; writes each spawn blueprint's defaults.
/// Not part of the normal test run.
/// </summary>
public sealed class SpawnPointProbe
{
    [Fact]
    public void Dump_spawn_point_defaults()
    {
        var output = Environment.GetEnvironmentVariable("SPAWN_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var lines = new List<string>();
        var paths = assets.AssetPaths.Where(p => p.Contains("/NPCSpawn_", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        lines.Add($"{paths.Count} spawn blueprints");
        foreach (var path in paths.Where(p => p.Contains("Gatekeeper", StringComparison.OrdinalIgnoreCase) || p.Contains("Peccary", StringComparison.OrdinalIgnoreCase)).Take(6))
        {
            lines.Add("=== " + path);
            assets.UseFileProvider(p =>
            {
                if (!p.TryLoadPackage(path, out var package)) return 0;
                foreach (var export in package.GetExports())
                {
                    if (!export.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    lines.Add(JsonConvert.SerializeObject(export, Formatting.Indented));
                }
                return 0;
            });
        }
        lines.AddRange(paths.Select(p => "path " + p));
        File.WriteAllLines(output, lines);
    }

    /// <summary>Writes <see cref="AbioticEditor.Core.WorldSaves.NpcSpawnCatalog"/> as JSON (SPAWN_CATALOG_OUT) for the bundled registry.</summary>
    [Fact]
    public void Write_spawn_catalog()
    {
        var output = Environment.GetEnvironmentVariable("SPAWN_CATALOG_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;
        var spawns = AbioticEditor.Core.WorldSaves.NpcSpawnCatalog.LoadFrom(assets);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(spawns.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value)));
    }
}
