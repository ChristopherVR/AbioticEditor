namespace AbioticEditor.Core.PlayerSaves;

public sealed record PlayerInventory(
    IReadOnlyList<InventoryItemSlot> Equipment,
    IReadOnlyList<InventoryItemSlot> Hotbar,
    IReadOnlyList<InventoryItemSlot> Main);
