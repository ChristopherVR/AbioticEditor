using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Source map a container was loaded from. <see cref="Deployed"/> entries live in
/// <c>DeployedObjectMap</c>; <see cref="Custom"/> entries live in <c>CustomInventoryMap</c>.
/// </summary>
public enum WorldContainerSource
{
    Deployed,
    Custom,

    /// <summary>On-board storage of a <c>VehicleMap</c> actor (keyed by the vehicle's spawn path).</summary>
    Vehicle,

    /// <summary>
    /// Not a file-save source at all: a live-editing view of a container currently loaded in a
    /// running game (see <c>LiveContainersSession</c>). Never appears in a saved file and is
    /// never written by <c>WorldSaveWriter</c> - the live session talks to the game directly
    /// over its own channel instead.
    /// </summary>
    Live,
}

/// <summary>
/// A single editable container in a world save. Backs onto either a deployed object
/// (chest, crate, freezer, etc.) or a named custom inventory.
/// </summary>
/// <param name="Id">
/// Map key: a hex GUID for deployed containers, a name string (e.g. <c>"Boxy"</c>) for
/// custom inventories.
/// </param>
/// <param name="Source">Which map this container came from.</param>
/// <param name="ClassName">
/// Blueprint asset name (e.g. <c>Deployed_StorageCrate_Makeshift_T2_C</c>) for deployed
/// containers; <c>null</c> for custom inventories.
/// </param>
/// <param name="Inventories">
/// One or more inventory grids attached to this container. In observed saves there's
/// almost always exactly one entry, but the underlying property is an array so we
/// preserve the count.
/// </param>
/// <param name="X">
/// World position in centimetres, 0 when unknown. File-save sources never populate this (a
/// deployed object's own actor transform is not read for the container list); only
/// <see cref="WorldContainerSource.Live"/> containers carry a real position, read straight
/// from the running game.
/// </param>
public sealed record WorldContainer(
    string Id,
    WorldContainerSource Source,
    string? ClassName,
    IReadOnlyList<WorldInventory> Inventories,
    double X = 0,
    double Y = 0,
    double Z = 0);

/// <summary>
/// A single inventory grid (one <c>SaveData_Inventories_Struct</c>) with its slots.
/// Slots reuse <see cref="InventoryItemSlot"/> because the underlying struct
/// (<c>Abiotic_InventoryItemSlotStruct</c>) is shared between player and world saves.
/// </summary>
public sealed record WorldInventory(IReadOnlyList<InventoryItemSlot> Slots);
