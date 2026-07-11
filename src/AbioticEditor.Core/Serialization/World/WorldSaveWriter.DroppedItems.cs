using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - dropped-item edits (add, remove, patch loose items in the world).
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Removes one <c>DroppedItemMap</c> entry - the editor equivalent of picking the
    /// item up off the ground. Returns true when the entry existed.
    /// </summary>
    public static bool RemoveDroppedItem(WorldSaveData data, string id)
    {
        if (data.Raw.Properties.FindByPrefix("DroppedItemMap")?.Property is not MapProperty mp
            || mp.Value is null)
        {
            return false;
        }
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key), id, StringComparison.Ordinal))
            {
                mp.Value.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds a new ground item to <c>DroppedItemMap</c> by <b>cloning an existing entry</b> -
    /// so the entry's struct layout is byte-for-byte what the game itself writes - and changing
    /// only the four things that make it a different drop: a fresh GUID map key, the item slot,
    /// the world location, and the no-despawn flag. Returns the new entry's id, or null when the
    /// map has no entry to clone (the writer never fabricates the struct from scratch, which
    /// would risk an unloadable save). The caller writes the save afterwards (keeping a .bak).
    /// </summary>
    public static string? AddDroppedItem(
        WorldSaveData data, InventoryItemSlot slot, double x, double y, double z, bool noDespawn = true)
    {
        if (data.Raw.Properties?.FindByPrefix("DroppedItemMap")?.Property is not MapProperty mp
            || mp.Value is null || mp.Value.Count == 0)
        {
            return null;
        }

        // Clone the whole save to a fresh object graph, then lift one entry out of the clone:
        // that entry shares no references with the live map, so grafting it back in (with new
        // leaf values) can't alias or corrupt the existing entries.
        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            data.Raw.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }
        if (clone.Properties?.FindByPrefix("DroppedItemMap")?.Property is not MapProperty cloneMap
            || cloneMap.Value is null || cloneMap.Value.Count == 0)
        {
            return null;
        }

        var template = cloneMap.Value[0];
        var key = template.Key;
        var value = template.Value;

        // Re-key with a fresh GUID, formatted like the keys already in this save.
        var existingKey = WorldSaveReader.ExtractMapKeyString(key);
        var newId = FormatGuidLike(existingKey, Guid.NewGuid());
        key.Value = new FString(newId);

        // Swap in the dropped item, its location, and the despawn flag; everything else stays
        // exactly as the cloned (game-authored) entry had it.
        if (value is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            if (ps.Properties.FindByPrefix("ItemData_")?.Property is StructProperty slotSp
                && slotSp.Value is PropertiesStruct slotPs)
            {
                ApplySlot(slotPs.Properties, slot);
            }
            if (ps.Properties.FindByPrefix("ItemLocation_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct vec)
            {
                var v = vec.Value;
                v.X = x;
                v.Y = y;
                v.Z = z;
                vec.Value = v;
            }
            SetBool(ps.Properties, "NoDespawn_", noDespawn);
        }

        mp.Value.Add(new KeyValuePair<FProperty, FProperty>(key, value));
        return newId;
    }

    /// <summary>
    /// Formats <paramref name="guid"/> to match the spelling of the save's existing dropped-item
    /// keys (hyphenated vs 32-char "N", upper vs lower case), so a new key looks native.
    /// </summary>
    private static string FormatGuidLike(string? sample, Guid guid)
    {
        var hasDashes = sample?.Contains('-') == true;
        var formatted = guid.ToString(hasDashes ? "D" : "N");
        var upper = sample is not null && sample.Any(char.IsLetter) && !sample.Any(char.IsLower);
        return upper ? formatted.ToUpperInvariant() : formatted;
    }

    /// <summary>
    /// Patches the item slot inside existing <c>DroppedItemMap</c> entries (matched by
    /// map key). Location/rotation/despawn flags are untouched.
    /// </summary>
    public static void ApplyDroppedItems(WorldSaveData data, IEnumerable<WorldDroppedItem> updated)
    {
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DroppedItemMap");
        if (pairs is null) return;

        var byId = updated.ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var item)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var itemData = ps.Properties.FindByPrefix("ItemData_");
            if (itemData?.Property is StructProperty slotSp && slotSp.Value is PropertiesStruct slotPs)
            {
                ApplySlot(slotPs.Properties, item.Slot);
            }
            SetBool(ps.Properties, "NoDespawn_", item.NoDespawn);
        }
    }

    public static int RemoveDroppedItems(WorldSaveData data, IReadOnlyCollection<string> ids)
    {
        var tag = data.Raw.Properties.FindByPrefix("DroppedItemMap");
        if (tag?.Property is not MapProperty mp || mp.Value is null || ids.Count == 0) return 0;

        var idSet = ids as ISet<string> ?? new HashSet<string>(ids, StringComparer.Ordinal);
        var removed = 0;
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            var key = WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key);
            if (key is not null && idSet.Contains(key))
            {
                mp.Value.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }
}
