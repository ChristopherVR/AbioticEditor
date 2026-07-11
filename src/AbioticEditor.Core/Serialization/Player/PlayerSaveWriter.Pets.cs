using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - carried-pet edits (the pet slots in the player inventory arrays).
public static partial class PlayerSaveWriter
{
    private static string ArrayPrefixFor(PetSlotKind kind) => kind switch
    {
        PetSlotKind.Equipment => "EquipmentInventory_",
        PetSlotKind.Hotbar => "HotbarInventory_",
        _ => "Inventory_",
    };

    private static ArrayProperty? GetInventoryArray(IList<FPropertyTag> root, PetSlotKind kind)
        => root.FindByPrefix(ArrayPrefixFor(kind))?.Property as ArrayProperty;

    private static bool SlotIsEmpty(PropertiesStruct slot)
    {
        if (slot.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            var row = rhp.Properties.GetString("RowName");
            return string.IsNullOrEmpty(row) || row is "None" or "Empty";
        }
        return true;
    }

    /// <summary>First empty slot index in a player inventory array, or -1 when full / absent.</summary>
    public static int FindFreeSlot(PlayerSaveData data, PetSlotKind kind)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return -1;
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr.GetValue(i) is StructProperty sp && sp.Value is PropertiesStruct ps && SlotIsEmpty(ps)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Patches an existing carried pet in place: name, health (durability), and XP / mutation.
    /// The slot must already hold a pet item (its DynamicProperties supply the element template).
    /// </summary>
    public static void ApplyCarriedPet(PlayerSaveData data, CarriedPet pet)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, pet.Slot) is not { Value: { } arr }) return;
        if (pet.Index < 0 || pet.Index >= arr.Length) return;
        if (arr.GetValue(pet.Index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", pet.ItemRow);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            var p = cdps.Properties;
            SetDouble(p, "CurrentItemDurability_", pet.Health, FullNames.CurrentItemDurability);
            SetDouble(p, "MaxItemDurability_", pet.MaxHealth, FullNames.MaxItemDurability);
            SetString(p, "PlayerMadeString_", pet.Name, pet.Name is null ? null : FullNames.PlayerMadeString);
            PetDynamicProperties.SetOrAdd(p, "XP", pet.Xp);
            PetDynamicProperties.SetOrAdd(p, "MutationProgress", pet.MutationProgress);
            PetDynamicProperties.SetOrAdd(p, "PetMutation", pet.PetMutation);
        }
    }

    /// <summary>
    /// Places a pet item into a free slot of <paramref name="kind"/> (or the given
    /// <paramref name="index"/>), building full pet data (health, XP, mutation). The
    /// DynamicProperties template is found anywhere in the save; when none exists the pet is
    /// written with variant + health only. Returns the slot index used, or -1 when no free slot.
    /// </summary>
    public static int AddCarriedPetToSlot(PlayerSaveData data, PetSlotKind kind, int index, CarriedPet pet)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return -1;

        if (index < 0)
        {
            index = FindFreeSlot(data, kind);
            if (index < 0) return -1;
        }
        if (index >= arr.Length) return -1;
        if (arr.GetValue(index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return -1;
        if (!SlotIsEmpty(sps)) return -1;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", pet.ItemRow);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            var p = cdps.Properties;
            SetInt(p, "CurrentStack_", 1, FullNames.CurrentStack);
            SetDouble(p, "CurrentItemDurability_", pet.Health, FullNames.CurrentItemDurability);
            SetDouble(p, "MaxItemDurability_", pet.MaxHealth, FullNames.MaxItemDurability);
            SetBool(p, "DynamicState_", true, FullNames.DynamicState);
            SetString(p, "AssetID_", Guid.NewGuid().ToString("N").ToUpperInvariant(), FullNames.AssetId);
            if (pet.Name is not null) SetString(p, "PlayerMadeString_", pet.Name, FullNames.PlayerMadeString);

            var template = PetDynamicProperties.CaptureTemplate(data.Raw);
            PetDynamicProperties.WriteArray(p, template, new[]
            {
                ("EDynamicProperty::XP", pet.Xp),
                ("EDynamicProperty::MutationProgress", pet.MutationProgress),
                ("EDynamicProperty::PetMutation", pet.PetMutation),
            });
        }
        return index;
    }

    /// <summary>Clears a carried pet's slot back to empty. Returns true when a pet was there.</summary>
    public static bool RemoveCarriedPet(PlayerSaveData data, PetSlotKind kind, int index)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return false;
        if (index < 0 || index >= arr.Length) return false;
        if (arr.GetValue(index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return false;
        if (SlotIsEmpty(sps)) return false;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", EmptySlotRowName);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            SetInt(cdps.Properties, "CurrentStack_", 0, FullNames.CurrentStack);
        }
        return true;
    }
}
