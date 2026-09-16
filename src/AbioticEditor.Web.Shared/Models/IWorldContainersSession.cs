using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open world-container editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>WorldContainersTab.razor</c> - and the sidebar slot editor it hands
/// slots to through <c>InventorySelectionService</c>/<c>SlotDragDropService</c> - uses, extracted
/// from <see cref="WorldSaveSession"/>'s existing container slice, so that tab binds to either the
/// file-backed session or <c>LiveContainersSession</c> with no changes beyond its parameter's
/// declared type.
/// </summary>
public interface IWorldContainersSession
{
    IReadOnlyList<WorldContainer> Containers { get; }
    bool CanEditContainers { get; }

    /// <summary>Deployables (for crafting-bench lookups the sidebar dismantle flow needs).</summary>
    IReadOnlyList<WorldDeployable> Deployables { get; }

    /// <summary>
    /// True for a live session: an edit reaches the running game immediately and cannot be
    /// undone with Revert, unlike a file session's edits, which stage until Save. Tabs use
    /// this to disable affordances that only make sense for a staged file (cross-container
    /// drag/drop with a player save that is not itself connected live).
    /// </summary>
    bool AppliesImmediately { get; }

    /// <summary>
    /// True when this process is allowed to change what it sees. Always true for a file
    /// session (opening a file for editing is never gated); reflects the running game's own
    /// host check for a live session, since only the host can write world containers there.
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// True when a slot edit can carry complete instance metadata (weapon coatings, pet
    /// progress, gameplay tags, custom variants) as one atomic write - see the
    /// <c>containers.setcomplete</c> wire command in <see cref="AbioticEditor.Core.LiveEditing.World.LiveContainersChannel"/>.
    /// Always true for the file session; a live session starts false and only turns on once it
    /// has actually seen the connected agent hand back <c>instanceMetadata</c> on a real slot, so
    /// an older agent never has a coating (or other metadata) edit silently dropped.
    /// </summary>
    bool SupportsCompleteItemWrites => true;

    /// <summary>
    /// Freeform status from the last edit. Null for a file session (the shell's own
    /// unsaved-changes wording already covers that); a live session uses it to say what just
    /// happened in the running game.
    /// </summary>
    string? Status { get; }

    bool TryGetContainerSlot(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, out InventoryItemSlot slot);

    /// <summary>Sets or clears one slot. A file session stages the change; a live session
    /// sends it to the running game immediately, then the caller should refresh.</summary>
    Task<bool> TrySetContainerSlotAsync(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, InventoryItemSlot slot, CancellationToken cancellationToken = default);

    Task<bool> TrySwapContainerSlotsAsync(WorldContainerSource source, string id, int inventoryIndex, int firstIndex, int secondIndex, CancellationToken cancellationToken = default);

    Task<bool> SortContainerSlotsAsync(WorldContainerSource source, string id, int inventoryIndex, CancellationToken cancellationToken = default);

    Task SetContainerSlotCountAsync(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, int count, CancellationToken cancellationToken = default);
}
