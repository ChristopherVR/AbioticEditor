using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Coordinates an atomic staged swap between an already-loaded player session and a
/// world-container session. It never writes either file: callers save or revert each
/// session through the ordinary backup-preserving controls.
/// </summary>
public static class InventoryTransferService
{
    public static bool TrySwapPlayerAndContainer(
        PlayerSaveSession player, PlayerInventoryArea playerArea, int playerSlotIndex,
        WorldSaveSession world, WorldContainerSource source, string containerId,
        int inventoryIndex, int containerSlotIndex)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(world);

        if (!player.TryGetInventorySlot(playerArea, playerSlotIndex, out var playerSlot)
            || !world.TryGetContainerSlot(source, containerId, inventoryIndex, containerSlotIndex, out var worldSlot))
        {
            return false;
        }

        // Both slots are validated before either is changed. The two setters only replace
        // in-memory projections, so a failed second setter can safely restore the first.
        if (!player.TrySetInventorySlot(playerArea, playerSlotIndex, worldSlot)) return false;
        if (world.TrySetContainerSlot(source, containerId, inventoryIndex, containerSlotIndex, playerSlot)) return true;
        player.TrySetInventorySlot(playerArea, playerSlotIndex, playerSlot);
        return false;
    }

    public static bool TryPickUpDroppedItem(
        PlayerSaveSession player, PlayerInventoryArea playerArea, int playerSlotIndex,
        WorldSaveSession world, string droppedItemId)
    {
        var dropped = world.DroppedItems.FirstOrDefault(item => string.Equals(item.Id, droppedItemId, StringComparison.Ordinal));
        if (dropped is null || dropped.Id.StartsWith("pending-", StringComparison.Ordinal)
            || !player.TryGetInventorySlot(playerArea, playerSlotIndex, out var destination)
            || !destination.IsEmpty) return false;
        if (!player.TrySetInventorySlot(playerArea, playerSlotIndex, dropped.Slot)) return false;
        world.RemoveDroppedItem(dropped.Id);
        if (!world.DroppedItems.Any(item => string.Equals(item.Id, dropped.Id, StringComparison.Ordinal))) return true;
        player.TrySetInventorySlot(playerArea, playerSlotIndex, destination);
        return false;
    }

    public static bool TryDropPlayerSlot(
        PlayerSaveSession player, PlayerInventoryArea playerArea, int playerSlotIndex,
        WorldSaveSession world, double x, double y, double z, out string pendingId)
    {
        pendingId = string.Empty;
        if (!player.TryGetInventorySlot(playerArea, playerSlotIndex, out var source) || source.IsEmpty
            || !world.TryAddDroppedItem(source, x, y, z, out pendingId)) return false;
        var empty = source with { ItemId = PlayerSaveWriter.EmptySlotRowName, Count = 0, Durability = 0,
            MaxDurability = 0, AmmoInMagazine = 0, LiquidLevel = 0, LiquidType = null,
            DynamicState = false, PlayerMadeString = null, AssetId = null };
        if (player.TrySetInventorySlot(playerArea, playerSlotIndex, empty)) return true;
        world.RemoveDroppedItem(pendingId);
        pendingId = string.Empty;
        return false;
    }
}
