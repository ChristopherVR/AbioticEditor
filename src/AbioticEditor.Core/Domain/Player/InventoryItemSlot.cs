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
    string? AssetId)
{
    public bool IsEmpty => string.IsNullOrEmpty(ItemId) || ItemId is "None" or "Empty";
    public double DurabilityPercent => MaxDurability > 0 ? Durability / MaxDurability : 0;
}
