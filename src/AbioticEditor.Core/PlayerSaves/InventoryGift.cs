namespace AbioticEditor.Core.PlayerSaves;

/// <summary>Outcome of dropping an item into a player's inventory.</summary>
public sealed record InventoryGiftResult(bool Ok, string Message, string Where);

/// <summary>
/// Places a single item into an already-loaded <see cref="PlayerSaveData"/>'s inventory, choosing
/// the first free backpack (main) slot and falling back to the hotbar. Used by cross-save transfers
/// (e.g. moving an item out of a world container into a player save). Mutates the player model via
/// <see cref="PlayerSaveWriter.ApplyInventory"/>; the caller writes the save afterwards (which keeps
/// a <c>.bak</c>). The source is never touched here - the caller removes it after a successful add so
/// the move stays a move, not a copy.
/// </summary>
public static class InventoryGift
{
    /// <summary>
    /// Drops <paramref name="item"/> into <paramref name="player"/>'s first empty backpack slot, or
    /// the first empty hotbar slot when the backpack is full. Returns <c>Ok = false</c> (and changes
    /// nothing) when the item is empty or there is no free slot in either place.
    /// </summary>
    public static InventoryGiftResult GiveToPlayer(PlayerSaveData player, InventoryItemSlot item)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (item is null || item.IsEmpty) return new(false, "There is no item in that slot to send.", string.Empty);

        var inv = player.Inventory;

        if (TryPlace(inv.Main, item, out var newMain, out var mainIndex))
        {
            PlayerSaveWriter.ApplyInventory(player, inv with { Main = newMain });
            return new(true, string.Empty, $"backpack slot {mainIndex}");
        }

        if (TryPlace(inv.Hotbar, item, out var newHotbar, out var hotbarIndex))
        {
            PlayerSaveWriter.ApplyInventory(player, inv with { Hotbar = newHotbar });
            return new(true, string.Empty, $"hotbar slot {hotbarIndex}");
        }

        return new(false,
            "That player's backpack and hotbar are both full. Free a slot in the player save and try again.",
            string.Empty);
    }

    // Copies item into the first empty slot of the list (preserving that slot's positional Index and
    // minting a fresh AssetID only when the item lacks one - the game tracks items by AssetID, so an
    // item carried across keeps its own id and a blank one would be invisible in-game).
    private static bool TryPlace(
        IReadOnlyList<InventoryItemSlot> slots, InventoryItemSlot item,
        out List<InventoryItemSlot> updated, out int index)
    {
        var copy = slots.ToList();
        for (var i = 0; i < copy.Count; i++)
        {
            if (!copy[i].IsEmpty) continue;
            var assetId = string.IsNullOrEmpty(item.AssetId)
                ? Guid.NewGuid().ToString("N").ToUpperInvariant()
                : item.AssetId;
            copy[i] = item with { Index = copy[i].Index, AssetId = assetId };
            updated = copy;
            index = copy[i].Index;
            return true;
        }
        updated = copy;
        index = -1;
        return false;
    }
}
