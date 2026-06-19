using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One in-game sector map: which sub-level it depicts and its texture.</summary>
/// <param name="Row">DT_MapPamphlets row, e.g. "Map_Office1".</param>
/// <param name="LevelFileName">The cooked level it depicts, e.g. "Facility_Office1".</param>
/// <param name="TexturePath">Game ref of the drawn map texture.</param>
public sealed record SectorMapInfo(string Row, string LevelFileName, string TexturePath);

/// <summary>
/// The game's own top-down sector maps, from DT_MapPamphlets (the pamphlet items the
/// player collects). Each row links a level (via a DT_Levels row handle) to a drawn
/// map texture, which the editor uses as the background for door positions.
/// </summary>
public static class SectorMapCatalog
{
    private const string LevelsTable = "AbioticFactor/Content/Blueprints/DataTables/Environment/DT_Levels";
    private const string PamphletsTable = "AbioticFactor/Content/Blueprints/DataTables/Environment/DT_MapPamphlets";

    /// <summary>Loads every pamphlet row that resolves to a level and a texture.</summary>
    public static IReadOnlyList<SectorMapInfo> LoadFrom(GameAssetProvider provider)
    {
        var result = new List<SectorMapInfo>();
        if (!provider.HasMappings) return result;

        try
        {
            // DT_Levels: row name -> LevelFileName (the cooked map name doors carry). Merge
            // mod/patch level tables that share the row struct, under any content root.
            var levelFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var levelsPrimary = provider.TryLoadDataTable(LevelsTable);
            AddLevelRows(levelsPrimary, levelFiles);
            foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, levelsPrimary?.RowStructName, new[] { LevelsTable }))
            {
                AddLevelRows(dt, levelFiles);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pamphletsPrimary = provider.TryLoadDataTable(PamphletsTable);
            AddPamphletRows(pamphletsPrimary, levelFiles, result, seen);
            foreach (var dt in ModTableDiscovery.LoadTablesByRowStruct(provider, pamphletsPrimary?.RowStructName, new[] { PamphletsTable }))
            {
                AddPamphletRows(dt, levelFiles, result, seen);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("SectorMaps", $"Could not load sector map catalog: {ex.Message}");
        }
        return result;
    }

    private static void AddLevelRows(UDataTable? dt, Dictionary<string, string> levelFiles)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            if (levelFiles.ContainsKey(kv.Key.Text)) continue;
            var file = kv.Value.Properties
                .FirstOrDefault(p => p.Name.Text.StartsWith("LevelFileName", StringComparison.Ordinal))
                ?.Tag?.GenericValue?.ToString();
            if (!string.IsNullOrEmpty(file))
            {
                levelFiles[kv.Key.Text] = file;
            }
        }
    }

    private static void AddPamphletRows(
        UDataTable? dt, Dictionary<string, string> levelFiles, List<SectorMapInfo> result, HashSet<string> seen)
    {
        if (dt is null) return;
        foreach (var kv in dt.RowMap)
        {
            if (string.IsNullOrEmpty(kv.Key.Text) || !seen.Add(kv.Key.Text)) continue;

            string? texture = null;
            string? levelRow = null;
            foreach (var p in kv.Value.Properties)
            {
                if (p.Name.Text.StartsWith("MapImage", StringComparison.Ordinal))
                {
                    texture = p.Tag?.GenericValue?.ToString();
                }
                else if (p.Name.Text.StartsWith("AssociatedLevel", StringComparison.Ordinal))
                {
                    var v = p.Tag?.GenericValue;
                    if (v is FScriptStruct ss) v = ss.StructType;
                    if (v is FStructFallback handle)
                    {
                        levelRow = handle.Properties
                            .FirstOrDefault(rp => rp.Name.Text == "RowName")
                            ?.Tag?.GenericValue?.ToString();
                    }
                }
            }

            if (texture is null) continue;

            // Game-data quirk: Map_Security/Map_Reactors/Map_Residence carry
            // stale or empty level handles (they point at Dam / None). When
            // the pamphlet's own row name ("Map_X") matches a DT_Levels row,
            // that beats a disagreeing handle.
            var candidate = kv.Key.Text.StartsWith("Map_", StringComparison.Ordinal)
                ? kv.Key.Text["Map_".Length..]
                : null;
            if (candidate is not null
                && levelFiles.ContainsKey(candidate)
                && !string.Equals(candidate, levelRow, StringComparison.OrdinalIgnoreCase))
            {
                levelRow = candidate;
            }
            if (levelRow is null || levelRow == "None") continue;
            if (!levelFiles.TryGetValue(levelRow, out var levelFile)) continue;
            result.Add(new SectorMapInfo(kv.Key.Text, levelFile, texture));
        }
    }

    /// <summary>The sector map depicting <paramref name="levelFileName"/>, or null.</summary>
    public static SectorMapInfo? ForLevel(IReadOnlyList<SectorMapInfo> maps, string? levelFileName)
        => string.IsNullOrEmpty(levelFileName)
            ? null
            : maps.FirstOrDefault(m => m.LevelFileName.Equals(levelFileName, StringComparison.OrdinalIgnoreCase));
}
