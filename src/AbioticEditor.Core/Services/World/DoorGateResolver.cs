using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The story gate a single placed door carries, read from its cooked level package.
/// </summary>
/// <param name="UnlockFlag">
/// World flag that unlocks the door (the blueprint's <c>WorldFlagToUnlock</c>). Null when
/// the door has no story gate at all.
/// </param>
/// <param name="RemainOpenFlag">
/// World flag that makes the door stay open for good, usually after a cutscene
/// (<c>WorldFlagToRemainOpen</c>). Null when the door has none.
/// </param>
public sealed record DoorStoryGate(string? UnlockFlag, string? RemainOpenFlag)
{
    /// <summary>Whether this door is story-controlled at all.</summary>
    public bool IsStoryGated => UnlockFlag is not null || RemainOpenFlag is not null;

    /// <summary>The flag a player should look for, preferring the unlock over stay-open.</summary>
    public string? PrimaryFlag => UnlockFlag ?? RemainOpenFlag;
}

/// <summary>
/// Resolves which world flag opens a given door. The save records door STATE only, and
/// <see cref="DoorClassCatalog"/> can only guess a lock kind per blueprint class - which
/// over-reports "story controlled", because the standard hinged door class CAN be gated
/// but almost never is. The truth is per placed instance: the level designer sets
/// <c>WorldFlagToUnlock</c> on the individual actor in the cooked .umap, so that is what
/// this reads. Across the shipped game only a handful of doors carry one.
///
/// Cached per map for the session, the same way <see cref="DoorLocationResolver"/> is.
/// </summary>
public static class DoorGateResolver
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, DoorStoryGate>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every door actor in <paramref name="mapName"/> that carries a story gate, keyed by
    /// actor instance name. Doors with no gate are absent, so a miss means "not story
    /// gated" - as does an empty result when the package cannot be read at all, which is
    /// why callers should treat a miss as "unknown" rather than proof of anything.
    /// </summary>
    public static IReadOnlyDictionary<string, DoorStoryGate> ForMap(GameAssetProvider provider, string? mapName)
    {
        var name = string.IsNullOrEmpty(mapName) ? "Facility" : mapName;
        return Cache.GetOrAdd(name, key => Load(provider, key));
    }

    /// <summary>The gate on one door, or null when it has none (or the map is unreadable).</summary>
    public static DoorStoryGate? Resolve(GameAssetProvider provider, string? mapName, string actorName)
        => ForMap(provider, mapName).TryGetValue(actorName, out var gate) ? gate : null;

    private static Dictionary<string, DoorStoryGate> Load(GameAssetProvider provider, string mapName)
    {
        var result = new Dictionary<string, DoorStoryGate>(StringComparer.OrdinalIgnoreCase);
        var packagePath = $"AbioticFactor/Content/Maps/{mapName}.umap";
        try
        {
            var pkg = provider.LoadPackageInternal(packagePath);
            foreach (var lazy in pkg.ExportsLazy)
            {
                CUE4Parse.UE4.Assets.Exports.UObject? export;
                try { export = lazy.Value; } catch { continue; }
                if (export is null) continue;

                var unlock = RowNameOf(export, "WorldFlagToUnlock");
                var remainOpen = RowNameOf(export, "WorldFlagToRemainOpen");
                if (unlock is null && remainOpen is null) continue;

                result[export.Name] = new DoorStoryGate(unlock, remainOpen);
            }
            Diagnostics.EditorLog.Info(
                "DoorGate", $"Found {result.Count} story-gated door(s) in {mapName}.");
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn(
                "DoorGate", $"Could not load {packagePath} for door story gates: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Pulls the flag name out of a DT_WorldFlags row handle property. The value is a
    /// struct with a DataTable and a RowName; the RowName IS the flag string the save's
    /// WorldFlags array stores. "None" means the designer left the handle empty.
    /// </summary>
    private static string? RowNameOf(CUE4Parse.UE4.Assets.Exports.UObject export, string propertyName)
    {
        var value = export.Properties
            .FirstOrDefault(p => p.Name.Text.StartsWith(propertyName, StringComparison.Ordinal))
            ?.Tag?.GenericValue;
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback handle) return null;

        var row = handle.Properties
            .FirstOrDefault(p => p.Name.Text == "RowName")
            ?.Tag?.GenericValue?.ToString();
        return string.IsNullOrEmpty(row) || row == "None" ? null : row;
    }
}
