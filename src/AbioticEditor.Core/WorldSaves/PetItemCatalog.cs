using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One pet in its inventory-item form (an <c>Item.Pet</c> row in ItemTable_Global).</summary>
/// <param name="ItemRow">The item row id, e.g. <c>Skink_Magma_Crafted</c>, <c>Pest_Leyak</c>.</param>
/// <param name="Friendly">Display name (matches a <see cref="PetCatalog"/> friendly name).</param>
/// <param name="IsWeaponForm">True for the BioCannon weapon forms (Skink / Magma Skink weapon).</param>
public sealed record PetItem(string ItemRow, string Friendly, bool IsWeaponForm);

/// <summary>
/// The inventory-item side of the pet system: pet items carried in a player's hotbar /
/// Companion slot. A carried pet is an ordinary item row tagged <c>Item.Pet</c>, with health
/// in <c>CurrentItemDurability_</c> and XP / mutation in <c>DynamicProperties_</c> (see
/// <see cref="PlayerSaves.CarriedPet"/>). This catalog bridges those item rows to the world
/// creature classes in <see cref="PetCatalog"/> by their shared friendly name, so a pet can be
/// moved between a world save (PetNPC) and a player save (hotbar item).
///
/// The 22 rows are curated (the item table has no NPC-class field); runtime pak enumeration of
/// <c>Item.Pet</c> rows could extend this later, but the curated set covers the shipped pets.
/// </summary>
public static class PetItemCatalog
{
    /// <summary>Default full health for a freshly placed carried pet (the true max is level-scaled in-game).</summary>
    public const double DefaultMaxHealth = 100;

    private static readonly PetItem[] _items =
    {
        new("pet_skink", "Skink", false),
        new("biocannon", "Skink", true),
        new("Skink_Magma", "Magma Skink", false),
        new("Skink_Magma_Crafted", "Magma Skink", true),
        new("pest", "Pest", false),
        new("Pest_Volatile", "Volatile Pest", false),
        new("Pest_Electro", "Electro Pest", false),
        new("Pest_Electro_Shield", "Electro Pest", false),
        new("Pest_Snow", "Snow Pest", false),
        new("Pest_Magma", "Magma Pest", false),
        new("Pest_Enlightened", "Enlightened Pest", false),
        new("Pest_Leyak", "Leyak Pest", false),
        new("Pest_Rat", "Rattus Pestis", false),
        new("Pest_Carbonated", "Carbonated Pest", false),
        new("Peccary", "Peccary", false),
        new("Sow", "Peccary Sow", false),
        new("Peccary_Mushroom", "Mushroom Peccary", false),
        new("Peccary_Snow", "Snow Peccary", false),
        new("Peccary_Armored", "Tareccary", false),
        new("Peccary_Volatile", "Volatile Peccary", false),
        new("Peccary_Electro", "Electro Peccary", false),
        new("Peccary_Alpha", "Peccary Alpha", false),
    };

    private static readonly Dictionary<string, PetItem> _byRow =
        _items.ToDictionary(i => i.ItemRow, StringComparer.OrdinalIgnoreCase);

    /// <summary>All known pet item rows.</summary>
    public static IReadOnlyList<PetItem> Items => _items;

    /// <summary>True when an inventory item row is a pet (held or weapon form).</summary>
    public static bool IsPetItem(string? itemRow)
        => !string.IsNullOrEmpty(itemRow) && _byRow.ContainsKey(itemRow!);

    /// <summary>The pet item for a row, or null.</summary>
    public static PetItem? ForRow(string? itemRow)
        => itemRow is not null && _byRow.TryGetValue(itemRow, out var i) ? i : null;

    /// <summary>Friendly name for a pet item row (e.g. "Magma Skink"), or null.</summary>
    public static string? FriendlyName(string? itemRow) => ForRow(itemRow)?.Friendly;

    /// <summary>Comparison key: lowercase alphanumerics only (so "Electro-Pest" == "Electro Pest").</summary>
    private static string Norm(string s)
        => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// The world creature class path for a carried pet item row (via the shared friendly
    /// name through <see cref="PetCatalog"/>). Null when no creature class matches.
    /// </summary>
    public static string? NpcClassFor(string? itemRow)
    {
        var friendly = FriendlyName(itemRow);
        if (friendly is null) return null;
        var key = Norm(friendly);
        var match = PetCatalog.Curated.FirstOrDefault(v => Norm(v.FriendlyName) == key);
        return match?.ClassPath;
    }

    /// <summary>
    /// The pet item row for a world creature class (or friendly name). Prefers the held form
    /// over the weapon form. Null when no pet item matches.
    /// </summary>
    public static string? ItemRowFor(string? npcClassOrFriendly, bool preferWeapon = false)
    {
        if (string.IsNullOrEmpty(npcClassOrFriendly)) return null;
        var friendly = PetCatalog.FriendlyName(npcClassOrFriendly) ?? npcClassOrFriendly;
        var key = Norm(friendly);
        var matches = _items.Where(i => Norm(i.Friendly) == key).ToList();
        if (matches.Count == 0) return null;
        return (matches.FirstOrDefault(i => i.IsWeaponForm == preferWeapon) ?? matches[0]).ItemRow;
    }
}
