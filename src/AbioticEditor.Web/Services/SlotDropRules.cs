using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The one place the web surfaces share their drag-and-drop semantics: role maps, the
/// game's slot-fit validation with the native app's localized messages, the fresh-item
/// fill, and the drag payload encoding for the data-drag attribute. Ported from the
/// native SlotInteractions + SlotSwap + InventorySlotViewModel.ValidateForSlot so the
/// inventory tab, transmog tab and sidebar palette cannot drift apart.
/// </summary>
public static class SlotDropRules
{
    /// <summary>Equipment slot semantics (verified against the game's own W_Inventory_EquipSlots widget).</summary>
    public static readonly IReadOnlyDictionary<int, string> EquipmentRoleMap = new Dictionary<int, string>
    {
        [0] = "CHEST",
        [1] = "HEAD",
        [2] = "LEGS",
        [3] = "BACK",
        [4] = "ARMS",
        [5] = "SUIT",
        [6] = "HEADLAMP",
        [7] = "TRINKET",
        [8] = "WATCH",
        [9] = "HACKER",
        [10] = "SHIELD",
        [11] = "TRINKET",
        [12] = "PET",
    };

    /// <summary>The 6 transmog slots mirror equipment indices 0-5 (W_Inventory_Transmog widget).</summary>
    public static readonly IReadOnlyDictionary<int, string> TransmogRoleMap = new Dictionary<int, string>
    {
        [0] = "CHEST",
        [1] = "HEAD",
        [2] = "LEGS",
        [3] = "BACK",
        [4] = "ARMS",
        [5] = "SUIT",
    };

    // E_InventorySlotType enumerator -> localized display-name resource (same table the
    // native EquipSlotLocalization uses for the "wrong slot" message).
    private static readonly Dictionary<int, string> SlotTypeNameKeys = new()
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

    /// <summary>The validation role of a player slot position (equipment/transmog only).</summary>
    public static string? RoleFor(PlayerInventoryArea area, int index) => area switch
    {
        PlayerInventoryArea.Equipment => EquipmentRoleMap.TryGetValue(index, out var role) ? role : null,
        PlayerInventoryArea.Transmog => TransmogRoleMap.TryGetValue(index, out var role) ? role : null,
        _ => null,
    };

    /// <summary>
    /// Enum-first slot-fit check with the native app's localized message: the item row's
    /// EquipSlot (E_InventorySlotType) must be the role's expected enumerator or the
    /// EquipmentSlots_All wildcard. Never id-prefix heuristics: suit_hazmat_casual is a
    /// LEGS item despite its name. Returns null when the item fits.
    /// </summary>
    /// <param name="skipValidation">The Settings &gt; Advanced "disable equip-slot checks"
    /// escape hatch (<see cref="HostAdvancedPreferences.SkipEquipSlotValidation"/>): when true,
    /// every item is treated as fitting.</param>
    public static string? ValidateForRole(HostLanguageService language, string? role, ItemCatalogEntry? entry, bool skipValidation = false)
    {
        if (skipValidation || role is null || entry is null) return null;
        if (EquipSlotTypes.ExpectedFor(role) is not { } expected) return null;
        var actual = entry.EquipSlot;
        if (actual == expected || actual == EquipSlotTypes.All) return null;
        var roleLabel = language.Resource($"EquipSlot_Role_{role}");
        return actual is 0 or 1
            ? language.Resource("EquipSlot_NotEquippable", roleLabel)
            : language.Resource("EquipSlot_WrongSlot", SlotTypeName(language, actual), roleLabel);
    }

    /// <summary>
    /// Prospective placement check for a target slot (native InventorySlotViewModel.ValidateForSlot):
    /// the equipment role fit plus the game's hotbar-only-pet rule. A pet item may live in the
    /// hotbar or the Companion equipment slot, but never loose in a Main-kind slot - the
    /// backpack, the transmog grid (native builds it as a Main list) or a storage container
    /// (a null area). Returns a human-readable problem or null when the item fits.
    /// </summary>
    public static string? ValidateForSlot(HostLanguageService language, PlayerInventoryArea? area, string? role, ItemCatalogEntry? entry, bool skipValidation = false)
    {
        if (skipValidation) return null;
        if (ValidateForRole(language, role, entry) is { } roleProblem) return roleProblem;
        var mainKind = area is null or PlayerInventoryArea.Backpack or PlayerInventoryArea.Transmog;
        if (mainKind && EquipSlotTypes.IsHotbarOnly(entry))
            return language.Resource("EquipSlot_PetsHotbarOnly");
        return null;
    }

    /// <summary>
    /// Swap validation both ways, like the native drop handler: the dragged item must fit
    /// the target slot and the displaced item must fit the source slot (role fit +
    /// hotbar-only pets). Returns the full localized "Blocked ..." status, or null when
    /// the swap may proceed.
    /// </summary>
    public static string? SwapProblem(
        HostLanguageService language, ItemCatalogService catalog,
        PlayerInventoryArea sourceArea, string? sourceRole, PlayerInventorySlotEdit source,
        PlayerInventoryArea targetArea, string? targetRole, PlayerInventorySlotEdit target,
        bool skipValidation = false)
    {
        if (skipValidation) return null;
        if (ValidateForSlot(language, targetArea, targetRole, catalog.Find(source.ItemId)) is { } problem)
            return language.Resource("Slot_MsgBlocked", problem);
        if (!target.IsEmpty && ValidateForSlot(language, sourceArea, sourceRole, catalog.Find(target.ItemId)) is { } displaced)
            return language.Resource("Slot_MsgBlockedSwap", displaced);
        return null;
    }

    /// <summary>
    /// Fill a slot with a fresh copy of a catalog item: full stack, full durability, no
    /// ammo/liquid, like the native palette give (SlotSwap.FillFromCatalog).
    /// </summary>
    public static void FillFromCatalog(PlayerInventorySlotEdit target, ItemCatalogEntry entry)
    {
        target.ItemId = entry.Id;
        target.Count = Math.Max(1, entry.StackSize);
        target.MaxDurability = entry.MaxDurability;
        target.Durability = entry.MaxDurability;
        target.AmmoInMagazine = 0;
        target.LiquidLevel = 0;
        target.LiquidType = null;
        target.DynamicState = false;
        target.PlayerMadeString = null;
        // Every item instance needs its own AssetID GUID: the game tracks/renders items by
        // it, so a blank or duplicated id occupies the slot but never shows in-game.
        target.AssetId = Guid.NewGuid().ToString("N").ToUpperInvariant();
    }

    /// <summary>data-drag payload for a palette item (also primes dataTransfer for Firefox).</summary>
    public static string PalettePayload(ItemCatalogEntry item) => $"palette:{item.Id}";

    /// <summary>data-drag payload for an occupied slot tile.</summary>
    public static string SlotPayload(PlayerInventoryArea area, PlayerInventorySlotEdit slot)
        => $"slot:{area}:{slot.Index}";

    private static string SlotTypeName(HostLanguageService language, int slotType)
        => SlotTypeNameKeys.TryGetValue(slotType, out var key) ? language.Resource(key) : EquipSlotTypes.NameOf(slotType);
}
