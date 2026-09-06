using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open world dropped-items editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>WorldDroppedItemsTab.razor</c> uses, extracted from
/// <see cref="WorldSaveSession"/>'s existing ground-item slice, so that tab binds to either the
/// file-backed session or <c>LiveDroppedItemsSession</c> with no changes beyond its parameter's
/// declared type.
///
/// <para>
/// A live session only truly supports listing and removing: there is no live restore, no live
/// count/no-despawn edit, and no live "drop a new item" - see
/// <see cref="AppliesImmediately"/>. The tab hides those affordances rather than call a
/// live session's non-functional stub, which throws <see cref="NotSupportedException"/> if
/// called anyway.
/// </para>
/// </summary>
public interface IWorldDroppedItemsSession
{
    IReadOnlyList<WorldDroppedItem> DroppedItems { get; }
    bool CanEditDroppedItems { get; }

    /// <summary>True for a live session: a removal despawns the item in the running game
    /// immediately and cannot be undone, unlike a file session's staged removal.</summary>
    bool AppliesImmediately { get; }

    /// <summary>True when this process is allowed to change what it sees. Always true for a
    /// file session; reflects the running game's own host check for a live session.</summary>
    bool IsHost { get; }

    /// <summary>Freeform status from the last edit. Null for a file session; a live session
    /// uses it to say what just happened in the running game.</summary>
    string? Status { get; }

    /// <summary>File only: stages a stack-count/no-despawn edit. Not offered live - see
    /// the interface remarks.</summary>
    void SetDroppedItem(string id, int count, bool noDespawn);

    /// <summary>Removes one item. A file session stages the removal; a live session despawns
    /// it in the running game immediately, then the caller should refresh.</summary>
    Task RemoveDroppedItemAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>File only: un-stages a pending removal. Not offered live - a live despawn
    /// cannot be undone.</summary>
    bool RestoreDroppedItem(WorldDroppedItem item);

    /// <summary>File only: stages a brand-new ground item. Not offered live.</summary>
    bool TryAddDroppedItem(InventoryItemSlot slot, double x, double y, double z, out string pendingId);

    /// <summary>File only: bulk-sets every item's despawn-timer flag. Not offered live.</summary>
    void SetAllDroppedNoDespawn(bool noDespawn);
}
