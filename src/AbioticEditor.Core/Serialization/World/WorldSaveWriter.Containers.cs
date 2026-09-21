using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - container and inventory edits (deployed, custom, and vehicle storage).
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Patches each container in <paramref name="updated"/> back into <paramref name="data"/>'s
    /// raw save tree.
    ///
    /// Containers are looked up by <see cref="WorldContainer.Id"/> and
    /// <see cref="WorldContainer.Source"/> against the original maps. Inventory entries
    /// are matched by ordinal (the array index in <c>ContainerInventories_</c>) and slots
    /// inside an inventory are matched by ordinal too. Out-of-range slots are ignored so
    /// the writer is robust to schema drift.
    /// </summary>
    public static void ApplyContainers(WorldSaveData data, IEnumerable<WorldContainer> updated)
    {
        var deployedById = BuildDeployedLookup(data);
        var customById = BuildCustomLookup(data);

        foreach (var container in updated)
        {
            switch (container.Source)
            {
                case WorldContainerSource.Deployed:
                    if (deployedById.TryGetValue(container.Id, out var deployableProps))
                    {
                        ApplyContainerInventoriesArray(deployableProps, container.Inventories, data.Raw);
                        ApplyContainerCustomName(deployableProps, container.Name);
                    }
                    break;
                case WorldContainerSource.Custom:
                    if (customById.TryGetValue(container.Id, out var inventoryStructProps)
                        && container.Inventories.Count > 0)
                    {
                        ApplyInventoryStruct(inventoryStructProps, container.Inventories[0], data.Raw);
                    }
                    break;
                case WorldContainerSource.Vehicle:
                    if (BuildMapLookup(data, "VehicleMap").TryGetValue(container.Id, out var vehicleProps))
                    {
                        ApplyContainerInventoriesArray(vehicleProps, container.Inventories, data.Raw);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Exact, hash-suffixed name of <c>CustomTextDisplay_</c> on <c>SaveData_Deployable_Struct</c>
    /// (surveyed 2026-09-17 against a real save - every one of over 600 deployed objects checked
    /// already carried this tag, empty or not, so creating it is a rare defensive fallback rather
    /// than the common case). Used only when a container's own tag is missing entirely; an
    /// existing tag is mutated in place via <see cref="SetCustomTextDisplay"/> instead, which
    /// preserves whichever of the two shapes (plain string or localizable text) that entry already
    /// uses - see that method's own remarks.
    /// </summary>
    private const string CustomTextDisplayFullName = "CustomTextDisplay_152_B59A50C74001B5D2234D9E9B0D7CAB7F";

    /// <summary>
    /// Sets (or clears) a deployed container's player-given name - the same
    /// <c>CustomTextDisplay_</c> field <see cref="ApplyDeployableCustomText"/> writes for a bed
    /// claim or sign, applied here as part of the container's own bulk <see cref="ApplyContainers"/>
    /// pass instead of that method's single-field immediate write, since a file edit stages until
    /// Save like every other container field. A null/empty name on an entry that never had the
    /// tag is a no-op - nothing to create a tag for.
    /// </summary>
    private static void ApplyContainerCustomName(IList<FPropertyTag> deployableProps, string? name)
    {
        var text = name ?? string.Empty;
        var existing = deployableProps.FindByPrefix("CustomTextDisplay_")?.Property;
        if (existing is null)
        {
            if (text.Length == 0) return;
            existing = FindOrCreate(deployableProps, "CustomTextDisplay_", CustomTextDisplayFullName, nameof(StrProperty));
        }
        SetCustomTextDisplay(existing, text);
    }

    private static Dictionary<string, IList<FPropertyTag>> BuildMapLookup(WorldSaveData data, string mapName)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, mapName);
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    private static Dictionary<string, IList<FPropertyTag>> BuildDeployedLookup(WorldSaveData data)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DeployedObjectMap");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    private static Dictionary<string, IList<FPropertyTag>> BuildCustomLookup(WorldSaveData data)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "CustomInventoryMap");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    // ---------- container / inventory writers ----------

    private static void ApplyContainerInventoriesArray(IList<FPropertyTag> deployableProps, IReadOnlyList<WorldInventory> updated, SaveGame? save = null)
    {
        var tag = deployableProps.FindByPrefix("ContainerInventories_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < updated.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;
            ApplyInventoryStruct(ps.Properties, updated[i], save);
        }
    }

    private static void ApplyInventoryStruct(IList<FPropertyTag> inventoryStructProps, WorldInventory inv, SaveGame? save = null)
    {
        var content = inventoryStructProps.FindByPrefix("InventoryContent_");
        if (content?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < inv.Slots.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;
            ApplySlot(ps.Properties, inv.Slots[i], save);
        }
    }

    /// <summary>
    /// Slot mutator. Kept private and parallel to <c>PlayerSaveWriter.ApplySlot</c> rather
    /// than reaching into it - the slot struct is shared but the writer surface isn't.
    /// </summary>
    private static void ApplySlot(IList<FPropertyTag> slotProps, InventoryItemSlot newSlot, SaveGame? save = null)
    {
        if (!string.IsNullOrEmpty(newSlot.ItemId))
        {
            var rowHandle = slotProps.FindByPrefix("ItemDataTable_");
            if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
            {
                var previousRow = rhPs.Properties.GetString("RowName");
                SetName(rhPs.Properties, "RowName", newSlot.ItemId);

                // Point the row handle at the table that actually holds this item. An empty slot
                // defaults to ItemTable_Pickups, which does NOT contain most catalog items, so an
                // item placed into one and left on that table fails to resolve in-game (the slot
                // reads as occupied but renders blank). Retarget ONLY when this write actually
                // changes what the slot holds (or the handle has no table at all). Every slot the
                // GAME wrote keeps its table even when the catalog would file the item elsewhere
                // (real saves carry game-written items on ItemTable_Pickups that the game resolves
                // fine), and the "Empty" sentinel row is never touched: the app re-applies every
                // slot on every save, so any "normalization" here rewrote saves the player never
                // asked to change (Nexus bug report #1 was exactly that class of churn). To fix an
                // item an old build left on the wrong table, re-place it in the slot.
                var currentTable = (rhPs.Properties.FindByPrefix("DataTable")?.Property as ObjectProperty)?.ObjectType?.ToString();
                var knownTable = Core.Items.ItemTableIndex.TableRefFor(newSlot.ItemId);
                var rowChanged = !string.Equals(previousRow, newSlot.ItemId, StringComparison.Ordinal);
                var onEmptySlotTable = string.IsNullOrEmpty(currentTable)
                    || currentTable.EndsWith("ItemTable_Pickups", StringComparison.OrdinalIgnoreCase);
                if (!newSlot.IsEmpty && rowChanged && onEmptySlotTable)
                {
                    PlayerSaveWriter.SetObjectPath(rhPs.Properties, "DataTable", knownTable ?? PlayerSaveWriter.ItemTableGlobalPath);
                }
            }
        }

        var changeable = slotProps.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs) return;

        // Same sparse-field handling as PlayerSaveWriter.ApplySlot: the game omits
        // default-valued ChangeableData members, so each tag is created when absent
        // (the inner member names are identical between player and world saves).
        var p = cPs.Properties;
        SetInt(p, "CurrentStack_", newSlot.Count, PlayerSaveWriter.FullNames.CurrentStack);
        SetDouble(p, "CurrentItemDurability_", newSlot.Durability, PlayerSaveWriter.FullNames.CurrentItemDurability);
        SetDouble(p, "MaxItemDurability_", newSlot.MaxDurability, PlayerSaveWriter.FullNames.MaxItemDurability);
        SetInt(p, "CurrentAmmoInMagazine_", newSlot.AmmoInMagazine, PlayerSaveWriter.FullNames.CurrentAmmoInMagazine);
        SetInt(p, "LiquidLevel_", newSlot.LiquidLevel, PlayerSaveWriter.FullNames.LiquidLevel);
        SetBool(p, "DynamicState_", newSlot.DynamicState, PlayerSaveWriter.FullNames.DynamicState);
        // Null means "no player text" - never create a tag just to hold null.
        SetString(p, "PlayerMadeString_", newSlot.PlayerMadeString,
            newSlot.PlayerMadeString is null ? null : PlayerSaveWriter.FullNames.PlayerMadeString);
        // AssetID is the per-instance GUID the game tracks items by; a freshly added item
        // carries a new id (SlotSwap.FillFromCatalog). Write it create-on-miss so a container
        // or dropped item the editor added registers in-game. Null leaves the existing id.
        SetString(p, "AssetID_", newSlot.AssetId,
            newSlot.AssetId is null ? null : PlayerSaveWriter.FullNames.AssetId);

        // Shared with player inventories so a container item can gain its first visual
        // variant even when the game delta-serialized the default row handle away.
        PlayerSaveWriter.ApplyVariantRowName(p, newSlot.VariantRowName);
        PetDynamicProperties.ApplyCoating(p, newSlot.CoatingIndex, newSlot.CoatingDurability, save);
    }

    /// <summary>Applies a chemistry bench flask through the same complete slot writer used by
    /// container edits: row handle, item table, stack count, and sparse changeable data all stay
    /// consistent when a saved flask is replaced or cleared.</summary>
    internal static void ApplyChemistryFlaskSlot(IList<FPropertyTag> slotProps, int index, string itemId)
    {
        var isEmpty = string.IsNullOrEmpty(itemId) || itemId is "Empty" or "None";
        ApplySlot(slotProps, new InventoryItemSlot(index, isEmpty ? "Empty" : itemId, isEmpty ? 0 : 1,
            0, 0, 0, 0, null, false, null, null));
    }

    // ---------- primitive setters ----------
}
