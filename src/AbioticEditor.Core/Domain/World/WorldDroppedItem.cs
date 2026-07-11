using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One entry of a world save's <c>DroppedItemMap</c>: an item lying on the ground.
/// </summary>
/// <param name="Id">Map key (GUID string).</param>
/// <param name="Slot">The single item (same slot struct as inventories, under <c>ItemData_</c>).</param>
/// <param name="NoDespawn">Whether the game's despawn timer is disabled for it.</param>
/// <param name="X">World position (from <c>ItemLocation_</c>; 0 when absent).</param>
public sealed record WorldDroppedItem(
    string Id, InventoryItemSlot Slot, bool NoDespawn,
    double X = 0, double Y = 0, double Z = 0)
{
    /// <summary>Straight-line distance to a point (player position), in UE units (cm).</summary>
    public double DistanceTo(double x, double y, double z)
        => Math.Sqrt((X - x) * (X - x) + (Y - y) * (Y - y) + (Z - z) * (Z - z));
}
