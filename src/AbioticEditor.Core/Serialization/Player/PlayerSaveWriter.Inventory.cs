using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - inventory, hotbar, equipment, and transmog slot edits.
public static partial class PlayerSaveWriter
{
    /// <summary>
    /// Patches the three inventory arrays in <paramref name="data"/>'s raw save tree to
    /// reflect <paramref name="updated"/>. Each slot in <paramref name="updated"/> is
    /// matched to the raw tree by array index; the writer walks the corresponding
    /// <c>ChangeableData</c> struct and updates CurrentStack / Durability / Ammo /
    /// LiquidLevel / DynamicState / PlayerMadeString. Item ID (RowName) is also patched.
    /// </summary>
    public static void ApplyInventory(PlayerSaveData data, PlayerInventory updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);

        ApplyInventoryArray(root, "EquipmentInventory_", updated.Equipment);
        ApplyInventoryArray(root, "HotbarInventory_", updated.Hotbar);
        ApplyInventoryArray(root, "Inventory_", updated.Main);
    }

    /// <summary>
    /// Patches the 6 <c>TransmogInventory_</c> slots from <paramref name="updated"/>,
    /// matched by array index - same in-place patching as the other inventory arrays.
    /// Saves without a transmog array (older game versions) are skipped silently.
    /// </summary>
    public static void ApplyTransmogSlots(PlayerSaveData data, IReadOnlyList<InventoryItemSlot> updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ApplyInventoryArray(root, "TransmogInventory_", updated);
    }

    /// <summary>
    /// Patches the 12 <c>TransmogVisibility_</c> bool flags in place, matched by index.
    /// Indices beyond the existing array length are skipped - the array is never resized.
    /// Saves without the property are skipped silently.
    /// </summary>
    public static void ApplyTransmogVisibility(PlayerSaveData data, IReadOnlyList<bool> visibility)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("TransmogVisibility_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < visibility.Count; i++)
        {
            array.Value.SetValue(visibility[i], i);
        }
    }

    /// <summary>
    /// The row name Abiotic Factor writes into an empty inventory slot's
    /// <c>ItemDataTable_.RowName</c> (see <see cref="InventoryItemSlot.IsEmpty"/>).
    /// </summary>
    public const string EmptySlotRowName = "Empty";

    /// <summary>
    /// Clears every slot of the equipment / hotbar / main / transmog arrays to the empty
    /// sentinel (<see cref="EmptySlotRowName"/>, stack 0), leaving the array structure
    /// intact. Used when fabricating a fresh (blank) player from an existing save's shape.
    /// </summary>
    public static void ClearAllInventory(PlayerSaveData data)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ClearInventoryArray(root, "EquipmentInventory_");
        ClearInventoryArray(root, "HotbarInventory_");
        ClearInventoryArray(root, "Inventory_");
        ClearInventoryArray(root, "TransmogInventory_");
    }

    private static void ClearInventoryArray(IList<FPropertyTag> root, string prefix)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            if (ps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rhSp
                && rhSp.Value is PropertiesStruct rhPs)
            {
                SetName(rhPs.Properties, "RowName", EmptySlotRowName);
            }

            if (ps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cSp
                && cSp.Value is PropertiesStruct cPs)
            {
                SetInt(cPs.Properties, "CurrentStack_", 0, FullNames.CurrentStack);
            }
        }
    }

    private static void ApplyInventoryArray(IList<FPropertyTag> root, string prefix, IReadOnlyList<InventoryItemSlot> updated)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < updated.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            var newSlot = updated[i];
            ApplySlot(ps.Properties, newSlot);
        }
    }

    private static void ApplySlot(IList<FPropertyTag> slotProps, InventoryItemSlot newSlot)
    {
        // RowName (item ID) - only patch if a valid id is provided; clearing to None
        // would require knowing the empty-slot sentinel AF uses, which differs.
        if (!string.IsNullOrEmpty(newSlot.ItemId))
        {
            var rowHandle = slotProps.FindByPrefix("ItemDataTable_");
            if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
            {
                SetName(rhPs.Properties, "RowName", newSlot.ItemId);

                // Point the row handle at the table that actually holds this item, but ONLY when it
                // still has the empty-slot default (ItemTable_Pickups) or nothing. An empty slot
                // defaults to ItemTable_Pickups, which does NOT contain catalog items, so the game
                // fails to resolve the row and renders the item blank (the slot reads as occupied -
                // hover sees RowName - but shows no icon). A slot already pointing at a real table
                // keeps it, so a plain stat edit stays byte-perfect; targeting the Pickups default
                // also REPAIRS an item an earlier editor build added with that wrong table.
                // ItemTableIndex gives the per-item table; without the catalog (CLI/tests) it falls
                // back to ItemTable_Global, the catalog's primary table.
                var currentTable = (rhPs.Properties.FindByPrefix("DataTable")?.Property as ObjectProperty)?.ObjectType?.ToString();
                if (string.IsNullOrEmpty(currentTable)
                    || currentTable.EndsWith("ItemTable_Pickups", StringComparison.OrdinalIgnoreCase))
                {
                    SetObjectPath(rhPs.Properties, "DataTable",
                        Items.ItemTableIndex.TableRefFor(newSlot.ItemId) ?? ItemTableGlobalPath);
                }
            }
        }

        var changeable = slotProps.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs) return;

        // ChangeableData is delta-serialized like everything else - a slot the game
        // wrote sparsely (e.g. an empty transmog slot has only AssetID_) is missing the
        // numeric/string tags entirely, so each edit creates the tag when absent.
        var p = cPs.Properties;
        SetInt(p, "CurrentStack_", newSlot.Count, FullNames.CurrentStack);
        SetDouble(p, "CurrentItemDurability_", newSlot.Durability, FullNames.CurrentItemDurability);
        SetDouble(p, "MaxItemDurability_", newSlot.MaxDurability, FullNames.MaxItemDurability);
        SetInt(p, "CurrentAmmoInMagazine_", newSlot.AmmoInMagazine, FullNames.CurrentAmmoInMagazine);
        SetInt(p, "LiquidLevel_", newSlot.LiquidLevel, FullNames.LiquidLevel);
        SetBool(p, "DynamicState_", newSlot.DynamicState, FullNames.DynamicState);
        // Null means "no player text" - never create a tag just to hold null.
        SetString(p, "PlayerMadeString_", newSlot.PlayerMadeString,
            newSlot.PlayerMadeString is null ? null : FullNames.PlayerMadeString);
        // AssetID is the per-instance GUID the game tracks items by. A freshly added item
        // carries a new id (see SlotSwap.FillFromCatalog); write it create-on-miss so the
        // game registers and renders the item. Null means "leave the slot's existing id".
        SetString(p, "AssetID_", newSlot.AssetId,
            newSlot.AssetId is null ? null : FullNames.AssetId);
    }
}
