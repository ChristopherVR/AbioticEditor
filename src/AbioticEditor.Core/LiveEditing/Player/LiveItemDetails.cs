using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>Instance fields shared by player and container inventories. Null details identify
/// an older agent; an explicit empty string clears a name or visual override.</summary>
public sealed record LiveItemDetails(int LiquidLevel = 0, string? LiquidType = null,
    bool DynamicState = false, string? PlayerMadeString = null, string? AssetId = null,
    string? VariantRowName = null)
{
    public static LiveItemDetails FromSlot(InventoryItemSlot slot) => new(slot.LiquidLevel,
        slot.LiquidType, slot.DynamicState, slot.PlayerMadeString ?? string.Empty,
        slot.AssetId, slot.VariantRowName ?? string.Empty);
}
