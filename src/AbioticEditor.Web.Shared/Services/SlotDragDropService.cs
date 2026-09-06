using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Shared state for an in-progress item drag, mirroring the native SlotInteractions model
/// where the payload rides the drag event between ANY two slot views. The web surfaces
/// (inventory tab, transmog tab, the sidebar item palette, shared slot grids) all publish
/// and read this one service, so a drag started on one surface can drop on any other -
/// exactly like the native editor. Drops stay authoritative: every drop handler re-runs
/// the placement validation, this service only carries the payload.
/// </summary>
public sealed class SlotDragDropService
{
    /// <summary>The catalog entry being dragged out of an item palette, if any.</summary>
    public ItemCatalogEntry? PaletteItem { get; private set; }

    /// <summary>The occupied slot being dragged, if any.</summary>
    public SlotDragSource? Source { get; private set; }

    public bool IsDragging => PaletteItem is not null || Source is not null;

    public event Action? Changed;

    public void BeginPaletteDrag(ItemCatalogEntry item)
    {
        PaletteItem = item;
        Source = null;
        Changed?.Invoke();
    }

    public void BeginSlotDrag(SlotDragSource source)
    {
        Source = source;
        PaletteItem = null;
        Changed?.Invoke();
    }

    public void End()
    {
        if (PaletteItem is null && Source is null) return;
        PaletteItem = null;
        Source = null;
        Changed?.Invoke();
    }
}

/// <summary>
/// A dragged slot: the session it belongs to, the inventory area it lives in and the
/// equipment/transmog role of that position (null for hotbar/backpack slots).
/// </summary>
/// <remarks>
/// <see cref="Session"/> is the narrow <see cref="IPlayerInventorySession"/> boundary, not the
/// concrete <see cref="PlayerSaveSession"/>, so a drag can start on either the file-backed
/// player editor or the live inventory/transmog tabs (both bind the same shared tab components
/// to this service) and still cross between inventory and transmog areas either way.
/// </remarks>
public sealed record SlotDragSource(
    IPlayerInventorySession Session,
    PlayerInventoryArea Area,
    PlayerInventorySlotEdit Slot,
    string? Role);
