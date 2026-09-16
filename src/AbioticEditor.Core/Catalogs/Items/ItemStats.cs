namespace AbioticEditor.Core.Items;

/// <summary>
/// The wiki-style stat block for one <see cref="ItemCatalogEntry"/>: whichever of the weapon,
/// armor, consumable, repair and salvage groups <c>ItemTable_Global</c> actually carries data for
/// that item. Every group is null when the row doesn't meaningfully populate it (e.g. a
/// non-weapon still carries a zeroed <c>WeaponData_</c> struct - see <see cref="ItemCatalog"/>'s
/// <c>BuildStats</c> gating), so a consumer only needs to check for null, never for a group's
/// fields all being zero.
/// </summary>
public sealed record ItemStats(
    WeaponStats? Weapon = null,
    ArmorStats? Armor = null,
    ConsumableStats? Consumable = null,
    RepairInfo? Repair = null,
    SalvageInfo? Salvage = null)
{
    /// <summary>True when every group is absent, so callers can skip rendering an empty block.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Weapon is null && Armor is null && Consumable is null && Repair is null && Salvage is null;
}

/// <summary>
/// Weapon numbers from <c>ItemTable_Global</c>'s <c>WeaponData_</c> struct. Only built when the
/// row's <c>IsWeapon_</c> flag is set - every item carries a <c>WeaponData_</c> struct with
/// placeholder defaults (10 damage, 0.5s between hits) whether or not it is actually a weapon.
/// </summary>
/// <param name="IsMelee"><c>WeaponData_ -&gt; Melee_</c>.</param>
/// <param name="DamagePerHit"><c>WeaponData_ -&gt; DamagePerHit_</c>.</param>
/// <param name="TimeBetweenAttacks">
/// Seconds between hits/shots (<c>WeaponData_ -&gt; TimeBetweenShots_</c>); lower is faster.
/// </param>
/// <param name="MagazineSize"><c>WeaponData_ -&gt; MagazineSize_</c> (melee weapons carry the field's default, 12; ignore it when <see cref="RequireAmmo"/> is false).</param>
/// <param name="RequireAmmo"><c>WeaponData_ -&gt; RequireAmmo_</c>.</param>
/// <param name="DamageType">
/// The damage-type class's short asset name (e.g. <c>Blunt_HEAVY</c>, <c>Bullet_Large</c>),
/// read from <c>WeaponData_ -&gt; DamageType_Hitscan_</c>. Null when the row points at the
/// generic base damage type.
/// </param>
public sealed record WeaponStats(
    bool IsMelee,
    double DamagePerHit,
    double TimeBetweenAttacks,
    int MagazineSize,
    bool RequireAmmo,
    string? DamageType);

/// <summary>
/// Armor numbers from <c>ItemTable_Global</c>'s <c>EquipmentData_</c> struct. Only built when at
/// least one of the numeric fields is non-zero or a set bonus row is present, so a shield/backpack
/// occupying an equipment slot with no actual protection doesn't show an all-zero armor block.
/// </summary>
/// <param name="ArmorBonus"><c>EquipmentData_ -&gt; ArmorBonus_</c>.</param>
/// <param name="HeatResist"><c>EquipmentData_ -&gt; HeatResist_</c>.</param>
/// <param name="ColdResist"><c>EquipmentData_ -&gt; ColdResist_</c>.</param>
/// <param name="SetBonusRow">
/// The set-bonus DataTable row name (<c>EquipmentData_ -&gt; SetBonus_ -&gt; RowName</c>), or null
/// when the item is not part of a set.
/// </param>
public sealed record ArmorStats(
    double ArmorBonus,
    double HeatResist,
    double ColdResist,
    string? SetBonusRow);

/// <summary>
/// Food/drink numbers from <c>ItemTable_Global</c>'s <c>ConsumableData_</c> struct. Only built
/// when at least one fill amount is non-zero or a buff is applied, so tools/weapons that carry the
/// same zeroed struct don't show an empty consumable block.
/// </summary>
/// <param name="HungerFill"><c>ConsumableData_ -&gt; HungerFill_</c>.</param>
/// <param name="ThirstFill"><c>ConsumableData_ -&gt; ThirstFill_</c>.</param>
/// <param name="FatigueFill"><c>ConsumableData_ -&gt; FatigueFill_</c>.</param>
/// <param name="SanityFill"><c>ConsumableData_ -&gt; SanityFill_</c>.</param>
/// <param name="BuffsApplied">
/// Buff tag names granted on consumption (<c>ConsumableData_ -&gt; BuffsToAdd_</c>).
/// </param>
public sealed record ConsumableStats(
    double HungerFill,
    double ThirstFill,
    double FatigueFill,
    double SanityFill,
    IReadOnlyList<string> BuffsApplied);

/// <summary>
/// The item + quantity range used to repair this item (<c>ItemTable_Global -&gt; RepairItem_</c>).
/// Only built when the row names an actual repair item.
/// </summary>
public sealed record RepairInfo(string ItemId, int QuantityMin, int QuantityMax);

/// <summary>
/// One entry of a salvage/scrap result (item + quantity range + drop chance), resolved from the
/// <c>DT_Salvage</c> row <c>ItemTable_Global -&gt; SalvageData_</c> points at.
/// </summary>
public sealed record SalvageDrop(string ItemId, int QuantityMin, int QuantityMax, double ChanceToDrop);

/// <summary>
/// Salvage/scrap results for this item. Only built when <c>SalvageData_</c> names a <c>DT_Salvage</c>
/// row that resolves to at least one drop.
/// </summary>
public sealed record SalvageInfo(IReadOnlyList<SalvageDrop> Drops);
