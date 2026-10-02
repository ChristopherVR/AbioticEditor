using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// What each kind of NPC spawn point makes: spawn blueprint (<c>NPCSpawn_Gatekeeper_Chieftain_C</c>)
/// to the short class of the creature it spawns (<c>NPC_Gatekeeper_Chieftain</c>). Read from each
/// spawn blueprint's <c>NPCsToSpawn</c> (a <c>DT_NPCList</c> row, first entry) and that row's
/// <c>NPCSpawnClass</c>, instead of guessing from the spawn point's name, which missed many
/// (the gatekeepers, the Dark Lens ones). Bundled in the registry for the browser build.
/// </summary>
public static class NpcSpawnCatalog
{
    private const string NpcListTable = "AbioticFactor/Content/Blueprints/DataTables/DT_NPCList";

    public static IReadOnlyDictionary<string, string> LoadFrom(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // DT_NPCList row -> spawned class.
            var rowClass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var list = provider.TryLoadDataTable(NpcListTable);
            foreach (var table in new[] { list }.Concat(ModTableDiscovery.LoadTablesByRowStruct(provider, list?.RowStructName, new[] { NpcListTable })))
            {
                if (table is null) continue;
                foreach (var (row, value) in table.RowMap)
                {
                    var spawn = value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("NPCSpawnClass_", StringComparison.Ordinal))?.Tag?.GenericValue?.ToString();
                    var shortClass = PetCatalog.ShortOf(spawn);
                    if (shortClass.Length > 0) rowClass.TryAdd(row.Text, shortClass);
                }
            }

            var spawns = provider.AssetPaths
                .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileNameWithoutExtension(p).StartsWith("NPCSpawn", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var path in spawns)
            {
                var row = provider.UseFileProvider(p =>
                {
                    if (!p.TryLoadPackage(path, out var package)) return null;
                    var defaults = package.GetExports().FirstOrDefault(e => e.Name.StartsWith("Default__", StringComparison.Ordinal));
                    return defaults is null ? null : FirstRow(defaults);
                });
                if (row is not null && rowClass.TryGetValue(row, out var creature))
                    result[Path.GetFileNameWithoutExtension(path) + "_C"] = creature;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Could not read what the NPC spawn points make: {ex.Message}");
        }
        return result;
    }

    /// <summary>The first <c>NPCsToSpawn</c> entry's <c>DT_NPCList</c> row, following the blueprint's parents when it inherits the list.</summary>
    private static string? FirstRow(UObject defaults)
    {
        for (UObject? current = defaults; current is not null; current = current.Template?.Load())
        {
            if (current.TryGetValue(out FStructFallback[] entries, "NPCsToSpawn") && entries.Length > 0)
            {
                foreach (var property in entries[0].Properties)
                {
                    if (!property.Name.Text.StartsWith("NPC_", StringComparison.Ordinal)) continue;
                    // A struct value comes wrapped (FScriptStruct around the row handle's fields).
                    var handle = property.Tag?.GenericValue switch
                    {
                        FScriptStruct { StructType: FStructFallback inner } => inner,
                        FStructFallback direct => direct,
                        _ => null,
                    };
                    if (handle is not null && handle.TryGetValue(out FName rowName, "RowName") && rowName.Text is { Length: > 0 } text && text != "None")
                        return text;
                }
            }
            if (current.Template is null) break;
        }
        return null;
    }

    /// <summary>The creature class a spawn point makes, from its key (<c>...NPCSpawn_Peccary_C_12</c>), or null.</summary>
    public static string? CreatureOf(IReadOnlyDictionary<string, string>? spawns, string? spawnKey)
    {
        if (spawns is null || string.IsNullOrEmpty(spawnKey)) return null;
        var name = spawnKey[(Math.Max(spawnKey.LastIndexOf('.'), spawnKey.LastIndexOf(':')) + 1)..];
        var cut = name.LastIndexOf("_C_", StringComparison.Ordinal);
        var cls = cut > 0 ? name[..(cut + 2)] : name;
        return spawns.TryGetValue(cls, out var creature) ? creature : null;
    }
}
