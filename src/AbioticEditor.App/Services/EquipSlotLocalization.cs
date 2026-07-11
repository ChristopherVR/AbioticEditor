using AbioticEditor.Core.Items;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized override for <see cref="EquipSlotTypes"/>'s slot-fit validation
/// message. The Core catalog stays English (the CLI source of truth); this reproduces the
/// same enum-first check with resx-backed text, mirroring the DoorLocalization pattern.
/// </summary>
public static class EquipSlotLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    private static readonly Dictionary<int, string> NameKeys = new()
    {
        [0] = "EquipSlot_Name_Hotbar",
        [1] = "EquipSlot_Name_Backpack",
        [2] = "EquipSlot_Name_Any",
        [5] = "EquipSlot_Name_Head",
        [6] = "EquipSlot_Name_Legs",
        [7] = "EquipSlot_Name_Back",
        [12] = "EquipSlot_Name_Arms",
        [13] = "EquipSlot_Name_Suit",
        [14] = "EquipSlot_Name_Chest",
        [15] = "EquipSlot_Name_Headlamp",
        [16] = "EquipSlot_Name_Trinket",
        [17] = "EquipSlot_Name_Watch",
        [18] = "EquipSlot_Name_Hacker",
        [19] = "EquipSlot_Name_Shield",
        [20] = "EquipSlot_Name_Trinket",
        [21] = "EquipSlot_Name_Companion",
    };

    /// <summary>As <see cref="EquipSlotTypes.ValidateForRole"/>, with a localized message.</summary>
    public static string? ValidateForRole(string? role, ItemCatalogEntry? entry)
    {
        if (role is null || entry is null) return null;
        if (EquipSlotTypes.ExpectedFor(role) is not { } expected) return null;

        var actual = entry.EquipSlot;
        if (actual == expected || actual == EquipSlotTypes.All) return null;

        var roleLabel = Loc[$"EquipSlot_Role_{role.ToUpperInvariant()}"];
        return actual is 0 or 1
            ? Loc.Format("EquipSlot_NotEquippable", roleLabel)
            : Loc.Format("EquipSlot_WrongSlot", NameOf(actual), roleLabel);
    }

    private static string NameOf(int slotType)
        => NameKeys.TryGetValue(slotType, out var key) ? Loc[key] : EquipSlotTypes.NameOf(slotType);
}
