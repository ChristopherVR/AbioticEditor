using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - placed-object structural edits (delete / add map entries, power-socket plug edits).
// These are the primitives behind StagedBaseEdits; the decisions (which records, which policy) live in
// Services/World. Nothing here is verified in-game: see docs/reference/research/base-building-group-operations.md.
public static partial class WorldSaveWriter
{
    /// <summary>The "no device" value the game stores in <c>PluggedInDeviceAssetID_</c>.</summary>
    internal const string NoPluggedDevice = "-1";

    /// <summary>
    /// Removes a player-built object (a GUID-keyed <c>DeployedObjectMap</c> entry). Returns false, changing
    /// nothing, for a key that is absent or is a level-placed actor path: those are re-placed by the level,
    /// so deleting the entry is not a deletion. This removes ONLY the entry; power records and other
    /// references are handled by the caller.
    /// </summary>
    public static bool RemovePlacedObject(WorldSaveData data, string key)
    {
        if (PlacedObjectCensus.KeyShape(key) != "guid32") return false;
        return WorldMapAccessor.RemoveEntry(data.Raw, "DeployedObjectMap", key);
    }

    /// <summary>Removes one <c>PowerSocketMap</c> record by its key. Returns false when absent.</summary>
    public static bool RemovePowerSocketRecord(WorldSaveData data, string socketId)
        => WorldMapAccessor.RemoveEntry(data.Raw, "PowerSocketMap", socketId);

    /// <summary>Appends a fully formed map pair (used for the entries a duplication creates).</summary>
    internal static bool AppendMapPair(SaveGame save, string mapName, KeyValuePair<FProperty, FProperty> pair)
    {
        var pairs = WorldMapAccessor.GetPairs(save, mapName);
        if (pairs is null) return false;
        pairs.Add(pair);
        return true;
    }

    /// <summary>
    /// Unplugs the given device keys from one socket record: a <c>PluggedInDeviceAssetID_</c> naming one of
    /// them becomes the game's own "none" value (<c>-1</c>), and matching elements are dropped from
    /// <c>ExtraPoweredDeviceAssetIDs_</c>. Returns true when the record changed.
    /// </summary>
    internal static bool UnplugDevices(IList<FPropertyTag> socketProps, IReadOnlySet<string> deviceKeys)
    {
        var changed = false;
        if (socketProps.FindByPrefix("PluggedInDeviceAssetID_")?.Property is { } plugged
            && plugged.Value?.ToString() is { } current && deviceKeys.Contains(current))
        {
            plugged.Value = new FString(NoPluggedDevice);
            changed = true;
        }
        if (socketProps.FindByPrefix("ExtraPoweredDeviceAssetIDs_")?.Property is ArrayProperty extras
            && FilterStringArray(extras, id => !deviceKeys.Contains(id)))
        {
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Rebuilds a string array keeping the elements for which <paramref name="keep"/> is true, re-using the
    /// existing element objects (so the element type is untouched). Returns true when something was dropped.
    /// </summary>
    internal static bool FilterStringArray(ArrayProperty array, Func<string, bool> keep)
    {
        if (array.Value is not { } items) return false;
        var kept = new List<object?>();
        foreach (var item in items)
        {
            var text = (item as FProperty)?.Value?.ToString() ?? item?.ToString() ?? string.Empty;
            if (keep(text)) kept.Add(item);
        }
        if (kept.Count == items.Length) return false;
        var replacement = Array.CreateInstance(items.GetType().GetElementType()!, kept.Count);
        for (var i = 0; i < kept.Count; i++) replacement.SetValue(kept[i], i);
        array.Value = replacement;
        return true;
    }

    /// <summary>
    /// Resets a bed's claim to the unclaimed shape (the bare claim separator). Returns false when the entry
    /// has no display-text member. A copied bed must never inherit its source's claim.
    /// </summary>
    internal static bool ClearBedClaim(IList<FPropertyTag> deployableProps)
        => SetCustomTextDisplay(deployableProps.FindByPrefix("CustomTextDisplay_")?.Property, WorldDeployable.ClaimSeparator);
}
