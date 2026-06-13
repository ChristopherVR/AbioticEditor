namespace AbioticEditor.Core.Items;

/// <summary>
/// The game's <c>E_InventorySlotType</c> enum (Blueprints/Data/E_InventorySlotType) and
/// the editor's role->slot-type validation rules. Every item row in
/// <c>ItemTable_Global</c> carries its slot type in <c>EquipmentData_ -> EquipSlot_</c>
/// (surfaced as <see cref="ItemCatalogEntry.EquipSlot"/>); validation is a simple
/// per-row enum comparison - never id-prefix heuristics (counter-example:
/// <c>suit_hazmat_casual</c> is a LEGS item despite its name).
/// See dotnet/docs/research-slot-types.md.
/// </summary>
public static class EquipSlotTypes
{
    /// <summary>EquipmentSlots_All - wildcard accepted by every equipment slot.</summary>
    public const int All = 2;

    /// <summary>
    /// Editor slot role (as used by the equipment/transmog role maps) -> the
    /// E_InventorySlotType enumerator number an item must carry to fit that slot.
    /// Both trinket UI slots accept slot-type 16 (no item row carries 20/Trinket2).
    /// </summary>
    private static readonly Dictionary<string, int> ExpectedByRole = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HEAD"] = 5,
        ["LEGS"] = 6,
        ["BACK"] = 7,
        ["ARMS"] = 12,
        ["SUIT"] = 13,
        ["CHEST"] = 14,
        ["HEADLAMP"] = 15,
        ["TRINKET"] = 16,
        ["WATCH"] = 17,
        ["HACKER"] = 18,
        ["SHIELD"] = 19,
        ["PET"] = 21,
    };

    /// <summary>E_InventorySlotType DisplayNameMap (enumerator number -> display name).</summary>
    private static readonly Dictionary<int, string> DisplayNames = new()
    {
        [0] = "Hotbar",
        [1] = "InventoryBackpack",
        [2] = "EquipmentSlots_All",
        [5] = "EquipmentSlot_Head",
        [6] = "EquipmentSlot_Legs",
        [7] = "EquipmentSlot_Backpack",
        [12] = "EquipmentSlot_Arms",
        [13] = "EquipmentSlot_Suit",
        [14] = "EquipmentSlot_Torso",
        [15] = "EquipmentSlot_Headlamp",
        [16] = "EquipmentSlot_Trinket",
        [17] = "EquipmentSlot_Wristwatch",
        [18] = "EquipmentSlot_Hacker",
        [19] = "EquipmentSlot_Shield",
        [20] = "EquipmentSlot_Trinket2",
        [21] = "EquipmentSlot_Companion",
    };

    /// <summary>
    /// The slot-type enumerator a role requires, or null for roles without an enum
    /// mapping (hotbar/main slots have no role at all).
    /// </summary>
    public static int? ExpectedFor(string? role)
        => role is not null && ExpectedByRole.TryGetValue(role, out var v) ? v : null;

    /// <summary>
    /// Human-readable name of a slot-type enumerator (for warnings). Unknown values
    /// (e.g. a new E_InventorySlotType member added by a future game version) degrade
    /// to "slot type {N}" and are logged once on the UNKWN channel.
    /// </summary>
    public static string NameOf(int slotType)
    {
        if (DisplayNames.TryGetValue(slotType, out var n)) return n;
        Diagnostics.EditorLog.UnknownData(
            "EquipSlot",
            slotType.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "enumerator not in E_InventorySlotType display map - newer game version?");
        return $"slot type {slotType}";
    }

    /// <summary>Does <paramref name="entry"/> fit a slot with the given role?</summary>
    public static bool Accepts(ItemCatalogEntry? entry, string? role)
        => ValidateForRole(role, entry) is null;

    /// <summary>
    /// Enum-first slot-fit check. Returns null when the item fits (its EquipSlot matches
    /// the role's expected type, or is the EquipmentSlots_All wildcard), otherwise a
    /// human-readable problem naming the item's actual slot. Role/Entry null -> null
    /// (nothing to validate).
    /// </summary>
    public static string? ValidateForRole(string? role, ItemCatalogEntry? entry)
    {
        if (role is null || entry is null) return null;
        if (ExpectedFor(role) is not { } expected) return null;

        var actual = entry.EquipSlot;
        if (actual == expected || actual == All) return null;

        return actual is 0 or 1
            ? $"Not equippable ({role} slot)"
            : $"{NameOf(actual)} item, not for {role} slot";
    }
}
