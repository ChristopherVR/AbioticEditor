using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveReader - container, deployable, and dropped-item reads.
public static partial class WorldSaveReader
{
    /// <summary>
    /// Lightweight pass over <c>DeployedObjectMap</c>: class + world position + inventory
    /// presence for every deployable (the base manager needs benches and furniture too,
    /// not just containers).
    /// </summary>
    private static IReadOnlyList<WorldDeployable> ReadDeployables(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (pairs is null) return Array.Empty<WorldDeployable>();

        var result = new List<WorldDeployable>(pairs.Count);
        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var className = ExtractClassName(ps.Properties);

            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("Transform_")?.Property is StructProperty tsp
                && tsp.Value is PropertiesStruct tps
                && tps.Properties.FindByPrefix("Translation")?.Property is StructProperty trsp
                && trsp.Value is VectorStruct vec)
            {
                x = vec.Value.X;
                y = vec.Value.Y;
                z = vec.Value.Z;
            }

            var hasInventory = false;
            var itemCount = 0;
            if (ps.Properties.FindByPrefix("ContainerInventories_")?.Property is ArrayProperty invArray
                && invArray.Value is { Length: > 0 })
            {
                hasInventory = true;
                for (var i = 0; i < invArray.Value.Length; i++)
                {
                    if (invArray.Value.GetValue(i) is StructProperty invSp && invSp.Value is PropertiesStruct invPs)
                    {
                        var inv = ReadInventoryStruct(invPs.Properties);
                        itemCount += inv?.Slots.Count(s => !s.IsEmpty && s.ItemId != "Empty") ?? 0;
                    }
                }
            }

            var customName = ps.Properties.FindByPrefix("CustomTextDisplay_")?.Property?.Value?.ToString();

            var upgrades = BenchUpgradeCatalog.ReadInstalledRows(ps.Properties);

            result.Add(new WorldDeployable(id, className, x, y, z, hasInventory, itemCount,
                string.IsNullOrWhiteSpace(customName) ? null : customName,
                upgrades.Count > 0 ? upgrades : null));
        }
        return result;
    }

    /// <summary>
    /// Reads <c>DroppedItemMap</c>: GUID -> struct with <c>ItemLocation_</c>,
    /// <c>ItemRotation_</c>, <c>ItemData_</c> (a standard inventory slot struct) and
    /// <c>NoDespawn_</c>.
    /// </summary>
    private static IReadOnlyList<WorldDroppedItem> ReadDroppedItems(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DroppedItemMap");
        if (pairs is null) return Array.Empty<WorldDroppedItem>();

        var result = new List<WorldDroppedItem>(pairs.Count);
        var index = 0;
        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var itemData = ps.Properties.FindByPrefix("ItemData_");
            var slot = ReadSlot(index++, itemData?.Property);
            var noDespawn = ps.Properties.TryGetBool("NoDespawn_") ?? false;

            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("ItemLocation_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }
            result.Add(new WorldDroppedItem(id, slot, noDespawn, x, y, z));
        }
        return result;
    }

    private static IEnumerable<WorldContainer> ReadDeployedContainers(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var key = ExtractMapKeyString(kvp.Key);
            if (key is null) continue;

            // Each value is a StructProperty around a SaveData_Deployable_Struct.
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            var className = ExtractClassName(ps.Properties);
            var inventories = ReadContainerInventoriesArray(ps.Properties);
            if (inventories.Count == 0) continue;

            yield return new WorldContainer(key, WorldContainerSource.Deployed, className, inventories);
        }
    }

    // ---------- CustomInventoryMap ----------

    private static IEnumerable<WorldContainer> ReadCustomInventoryContainers(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "CustomInventoryMap");
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var key = ExtractMapKeyString(kvp.Key);
            if (key is null) continue;

            // The value is itself a single SaveData_Inventories_Struct (not an array of them).
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            var inv = ReadInventoryStruct(ps.Properties);
            if (inv is null) continue;

            yield return new WorldContainer(
                key,
                WorldContainerSource.Custom,
                ClassName: null,
                Inventories: new[] { inv });
        }
    }

    // ---------- WorldFlags ----------

    /// <summary>
    /// Reads the <c>ContainerInventories_*</c> ArrayProperty (an array of
    /// <c>SaveData_Inventories_Struct</c>) into a flat list of inventories.
    /// </summary>
    private static IReadOnlyList<WorldInventory> ReadContainerInventoriesArray(IList<FPropertyTag> deployableProps)
    {
        var tag = deployableProps.FindByPrefix("ContainerInventories_");
        if (tag?.Property is not ArrayProperty array || array.Value is null || array.Value.Length == 0)
            return Array.Empty<WorldInventory>();

        var result = new List<WorldInventory>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            // Array elements for a struct-typed array are StructProperty wrappers whose Value
            // is the IStructData payload (PropertiesStruct for SaveData_Inventories_Struct).
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            var inv = ReadInventoryStruct(ps.Properties);
            if (inv is not null) result.Add(inv);
        }
        return result;
    }

    /// <summary>
    /// Reads one <c>SaveData_Inventories_Struct</c>: an <c>InventoryContent_*</c>
    /// ArrayProperty of <c>Abiotic_InventoryItemSlotStruct</c> elements.
    /// </summary>
    internal static WorldInventory? ReadInventoryStruct(IList<FPropertyTag> inventoriesStructProps)
    {
        var content = inventoriesStructProps.FindByPrefix("InventoryContent_");
        if (content?.Property is not ArrayProperty array || array.Value is null)
            return null;

        var slots = new List<InventoryItemSlot>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            slots.Add(ReadSlot(i, array.Value.GetValue(i)));
        }
        return new WorldInventory(slots);
    }

    /// <summary>
    /// Reads one slot. Mirrors <c>PlayerSaveReader.ReadSlot</c> - kept private here so we
    /// don't take a dependency on its internals; the underlying struct is identical.
    /// </summary>
    private static InventoryItemSlot ReadSlot(int index, object? element)
    {
        if (element is not StructProperty outer || outer.Value is not PropertiesStruct ps)
            return EmptySlot(index);

        string? itemId = null;
        var rowHandle = ps.Properties.FindByPrefix("ItemDataTable_");
        if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
        {
            itemId = rhPs.Properties.GetString("RowName");
        }

        var changeable = ps.Properties.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs)
        {
            return new InventoryItemSlot(index, itemId, 1, 0, 0, 0, 0, null, false, null, null);
        }

        var p = cPs.Properties;
        return new InventoryItemSlot(
            Index: index,
            ItemId: itemId,
            Count: (int)p.GetLong("CurrentStack_", 1),
            Durability: p.GetDouble("CurrentItemDurability_"),
            MaxDurability: p.GetDouble("MaxItemDurability_"),
            AmmoInMagazine: (int)p.GetLong("CurrentAmmoInMagazine_"),
            LiquidLevel: (int)p.GetLong("LiquidLevel_"),
            LiquidType: p.GetEnumString("CurrentLiquid_"),
            DynamicState: p.GetBool("DynamicState_"),
            PlayerMadeString: p.GetString("PlayerMadeString_"),
            AssetId: p.GetString("AssetID_"));
    }

    private static InventoryItemSlot EmptySlot(int index)
        => new(index, null, 0, 0, 0, 0, 0, null, false, null, null);

    private static string? ExtractClassName(IList<FPropertyTag> deployableProps)
    {
        var classTag = deployableProps.FindByPrefix("Class_");
        if (classTag?.Property?.Value is SoftObjectPath softPath)
        {
            return softPath.AssetName?.Value;
        }
        // Some builds may unbox SoftObjectProperty differently - fall back to ToString.
        return classTag?.Property?.Value?.ToString();
    }

    // ---------- map / primitive accessors ----------
}
