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
/// creature classes in <see cref="PetCatalog"/> so a pet can be moved between a world save
/// (PetNPC) and a player save (hotbar item).
///
/// When the game paks are mounted (<see cref="PetCatalog.ApplyGameData"/>), the item list
/// and the item&lt;-&gt;creature bridge come straight from the game's <c>Item.Pet</c> rows and
/// <c>DT_Pets</c>/<c>DT_NPCList</c>, so pets added by future updates work with no code
/// change. The curated table below is the offline fallback.
/// </summary>
public static class PetItemCatalog
{
    /// <summary>Default full health for a freshly placed carried pet (the true max is level-scaled in-game).</summary>
    public const double DefaultMaxHealth = 100;

    private static readonly PetItem[] _curated =
    {
        new("pet_skink", "Skink", false),
        new("biocannon", "Skink", true),
        new("Skink_Magma", "Magma Skink", false),
        new("Skink_Magma_Crafted", "Magma Skink", true),
        new("Skink_Mushroom", "Verdant Skink", false),
        new("Skink_Mushroom_Crafted", "Verdant Skink", true),
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
        // The Ogi family + carried Lamogi forms (anniversary update, v1.4.0).
        new("WinterSprite", "Lamogi", false),
        new("Lamogi_Plated", "Sir Ogi", false),
        new("Lamogi_Speedy", "Speedogi", false),
    };

    private static readonly Dictionary<string, PetItem> _curatedByRow =
        _curated.ToDictionary(i => i.ItemRow, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All known pet item rows: the game's own <c>Item.Pet</c> list when the pet tables are
    /// loaded, otherwise the curated fallback.
    /// </summary>
    public static IReadOnlyList<PetItem> Items
    {
        get
        {
            var data = PetCatalog.AppliedGameData;
            if (data is null) return _curated;
            var items = new List<PetItem>();
            foreach (var d in data.Definitions)
            {
                if (d.ItemRow is null) continue;
                items.Add(new PetItem(d.ItemRow, d.DisplayName, d.IsWeaponForm));
            }
            return items.Count > 0 ? items : _curated;
        }
    }

    /// <summary>True when an inventory item row is a pet (held or weapon form).</summary>
    public static bool IsPetItem(string? itemRow)
        => ForRow(itemRow) is not null;

    /// <summary>The pet item for a row, or null.</summary>
    public static PetItem? ForRow(string? itemRow)
    {
        if (string.IsNullOrEmpty(itemRow)) return null;
        if (PetCatalog.AppliedGameData?.ByItemRow(itemRow) is { } d)
        {
            return new PetItem(d.ItemRow ?? itemRow!, d.DisplayName, d.IsWeaponForm);
        }
        return _curatedByRow.TryGetValue(itemRow!, out var i) ? i : null;
    }

    /// <summary>Friendly name for a pet item row (e.g. "Magma Skink"), or null.</summary>
    public static string? FriendlyName(string? itemRow) => ForRow(itemRow)?.Friendly;

    /// <summary>Comparison key: lowercase alphanumerics only (so "Electro-Pest" == "Electro Pest").</summary>
    private static string Norm(string s)
        => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// The world creature class path for a carried pet item row. With the live pet tables
    /// loaded this is the game's own item -> NPC class mapping; offline it goes through the
    /// shared friendly name in <see cref="PetCatalog"/>. Null when no creature class matches.
    /// </summary>
    public static string? NpcClassFor(string? itemRow)
    {
        if (PetCatalog.AppliedGameData?.ByItemRow(itemRow) is { ClassPath: not null } d) return d.ClassPath;

        var item = ForRow(itemRow);
        if (item is null) return null;
        var key = Norm(item.Friendly);
        // A weapon-form item maps to the crafted creature ("Skink (Weapon)") when curated.
        if (item.IsWeaponForm
            && PetCatalog.Curated.FirstOrDefault(v => Norm(v.FriendlyName) == key + "weapon") is { } weapon)
        {
            return weapon.ClassPath;
        }
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

        if (PetCatalog.AppliedGameData is { } data)
        {
            var direct = data.ByClass(npcClassOrFriendly);
            if (direct?.ItemRow is not null && direct.IsWeaponForm == preferWeapon) return direct.ItemRow;
            // Same pet in the requested form (held vs weapon), matched by display name.
            var wanted = Norm(direct?.DisplayName ?? npcClassOrFriendly!);
            var sameName = data.Definitions
                .Where(d => d.ItemRow is not null && Norm(d.DisplayName) == wanted)
                .ToList();
            if (sameName.Count > 0)
            {
                return (sameName.FirstOrDefault(d => d.IsWeaponForm == preferWeapon) ?? sameName[0]).ItemRow;
            }
            if (direct?.ItemRow is not null) return direct.ItemRow;
        }

        var friendly = PetCatalog.FriendlyName(npcClassOrFriendly) ?? npcClassOrFriendly;
        var key = Norm(StripWeaponSuffix(friendly!));
        var matches = _curated.Where(i => Norm(i.Friendly) == key).ToList();
        if (matches.Count == 0) return null;
        return (matches.FirstOrDefault(i => i.IsWeaponForm == preferWeapon) ?? matches[0]).ItemRow;
    }

    private static string StripWeaponSuffix(string name)
        => name.Replace("(Weapon)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
}
