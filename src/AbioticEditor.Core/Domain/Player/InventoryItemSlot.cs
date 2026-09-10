namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One slot from an inventory array (Equipment / Hotbar / Main). When <see cref="ItemId"/>
/// is null/empty the slot is empty. <see cref="Index"/> is the slot's position in its
/// containing array.
/// </summary>
public sealed record InventoryItemSlot(
    int Index,
    string? ItemId,
    int Count,
    double Durability,
    double MaxDurability,
    int AmmoInMagazine,
    int LiquidLevel,
    string? LiquidType,
    bool DynamicState,
    string? PlayerMadeString,
    string? AssetId,
    // Which visual variant this instance shows (a poster's artwork, a helmet's paint color,
    // ...): the RowName of the item struct's TextureVariantRow_ handle into
    // DT_TextureVariants, independent of ItemId. Null when the game never wrote one for this
    // instance (most items), in which case there is nothing here yet to change.
    string? VariantRowName = null)
{
    public bool IsEmpty => string.IsNullOrEmpty(ItemId) || ItemId is "None" or "Empty";
    public double DurabilityPercent => MaxDurability > 0 ? Durability / MaxDurability : 0;
}
