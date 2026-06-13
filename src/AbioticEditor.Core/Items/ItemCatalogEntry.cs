namespace AbioticEditor.Core.Items;

/// <summary>
/// One row from <c>ItemTable_Global</c>: the static definition of an Abiotic Factor item.
/// </summary>
/// <param name="EquipSlot">
/// The item's <c>E_InventorySlotType</c> enumerator number from
/// <c>EquipmentData_100_* -> EquipSlot_5_*</c> (e.g. 5 = EquipmentSlot_Head). 0/1 mean the
/// item is not equippable; 2 is the EquipmentSlots_All wildcard. See
/// <see cref="EquipSlotTypes"/>.
/// </param>
public sealed record ItemCatalogEntry(
    string Id,
    string DisplayName,
    string? Description,
    string? IconAssetPath,
    int StackSize,
    double MaxDurability,
    bool IsWeapon,
    double Weight,
    IReadOnlyList<string> Tags,
    int ContainerCapacity = 0,
    int EquipSlot = 0,
    int MaxLiquid = 0,
    IReadOnlyList<int>? AllowedLiquids = null)
{
    /// <summary>Liquid types this container accepts (E_LiquidType enumerator numbers).</summary>
    public IReadOnlyList<int> AllowedLiquidList => AllowedLiquids ?? Array.Empty<int>();

    /// <summary>
    /// A concise, human-readable identity (e.g. <c>shelf_m (Medium Shelf)</c>). Overrides the
    /// record's auto-generated <c>ToString</c>, which dumps every member and - worse - renders
    /// the collection properties (<see cref="Tags"/>, <see cref="AllowedLiquidList"/>) as their
    /// .NET type names (<c>System.String[]</c>, <c>List`1[System.Int32]</c>). That string leaks
    /// into diagnostic logs whenever an entry is logged, so a clean form is the right default.
    /// </summary>
    public override string ToString()
        => string.IsNullOrEmpty(DisplayName) || string.Equals(DisplayName, Id, StringComparison.Ordinal)
            ? Id
            : $"{Id} ({DisplayName})";
}
