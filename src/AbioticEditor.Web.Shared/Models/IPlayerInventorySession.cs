using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-inventory editing session, mirroring the narrow
/// interface pattern <see cref="IPlayerVitalsSession"/>/<see cref="IPlayerSkillsSession"/>
/// already use (see <c>PlayerVitals.cs</c>). Exactly the members <c>PlayerInventoryTab.razor</c>,
/// the sidebar <c>InventorySlotEditor.razor</c> and the shared drag/drop plumbing
/// (<c>SlotDragDropService</c>, <c>InventoryTransferService</c>) actually use, extracted from
/// <see cref="PlayerSaveSession"/>'s existing inventory slice, so those widgets bind to either
/// the file-backed session or a live one with no change beyond the declared parameter type.
///
/// <see cref="IPlayerTransmogSession"/> extends this interface rather than standing beside it:
/// transmog is just a fourth <see cref="PlayerInventoryArea"/> alongside equipment/hotbar/
/// backpack in <see cref="PlayerSaveSession"/> already, and a slot can be dragged between any of
/// them, so one session object has to answer to both boundaries for that drag to work.
/// </summary>
public interface IPlayerInventorySession
{
    IReadOnlyList<PlayerInventorySlotEdit> Equipment { get; }
    IReadOnlyList<PlayerInventorySlotEdit> Hotbar { get; }
    IReadOnlyList<PlayerInventorySlotEdit> Backpack { get; }

    /// <summary>Every item id this player has ever picked up or crafted, for the sidebar
    /// palette's search. Empty (never null) when no vocabulary is available.</summary>
    IReadOnlyList<string> ItemVocabulary { get; }

    ItemUpgradeCatalog ItemUpgrades { get; }

    /// <summary>Money (and limb health) as edited from the pockets footer. A live session
    /// exposes this only so the shared markup type-checks; the tab hides the money field
    /// entirely when <see cref="AppliesImmediately"/> is true rather than editing a value that
    /// would silently not reach the game (money lives on the VITALS tab instead, which does
    /// push live).</summary>
    PlayerVitals Vitals { get; }

    /// <summary>The saved respawn point, used only to measure distance to nearby world-dropped
    /// items when a (file-backed) world session is also open. A live session never has that
    /// world session wired in, so this is never actually read there; it can return a fixed
    /// placeholder.</summary>
    PlayerRespawnEdit Respawn { get; }

    /// <summary>Steam account id inferred from a player save's filename, or null when there is
    /// no file to infer one from (a live session).</summary>
    string? SteamIdentifier { get; }

    /// <summary>The save file's path, used only to key the file-only "sibling world benches"
    /// lookup. A live session (<see cref="AppliesImmediately"/>) skips that lookup entirely, so
    /// this can return a harmless placeholder there.</summary>
    string Path { get; }

    string? Status { get; }

    /// <summary>
    /// Raised when this session's slot data changed for a reason other than whatever the
    /// currently-open tab itself just did (a live session refreshing after a DIFFERENT tab wrote
    /// to it, e.g. a container-to-player transfer started from <c>WorldContainersTab</c>, or the
    /// host's own periodic background poll). No-op by default: the file session's every write
    /// already happens synchronously inside the same tab that made it, and that tab's own explicit
    /// <c>StateHasChanged()</c> after each edit already covers it, so it never needs to raise this.
    /// A live session's own <c>Changed</c> event (already raised after every refresh) fills this in.
    /// </summary>
    event Action? Changed
    {
        add { }
        remove { }
    }

    /// <summary>
    /// True for a session backed by a running game: every mutation below already reached the
    /// game by the time it returns, so the shared tab shows an "applied live" <see cref="Status"/>
    /// instead of relying on the host's page-level SAVE button. False for the file session,
    /// which stages edits in place until the caller calls its own SaveAsync.
    /// </summary>
    bool AppliesImmediately { get; }

    /// <summary>
    /// True when a slot edit can carry complete instance metadata (weapon coatings, pet
    /// progress, gameplay tags, custom variants) as one atomic write - see the
    /// <c>inventory.setcomplete</c> wire command in <see cref="AbioticEditor.Core.LiveEditing.Player.LiveInventoryChannel"/>.
    /// Always true for the file session, whose writer always serializes complete metadata. A
    /// live session starts false and only turns on once it has actually seen the connected
    /// agent hand back <c>instanceMetadata</c> on a real slot, so an older agent that predates
    /// that wire shape never has a coating (or other metadata) edit silently dropped.
    /// </summary>
    bool SupportsCompleteItemWrites => true;

    /// <summary>Recomputes <see cref="Status"/> from whatever changed. For the file session this
    /// is the existing "Unsaved changes" staging signal; a live session has nothing to stage, so
    /// this is a no-op there (its mutation methods set <see cref="Status"/> themselves).</summary>
    void MarkChanged();

    bool TryGetInventorySlot(PlayerInventoryArea area, int index, out InventoryItemSlot slot);

    /// <summary>Overwrites one slot's stored fields. For the live session this only updates the
    /// local mirror (used by <see cref="AbioticEditor.Web.Services"/>'s world-container/dropped-
    /// item transfer helpers, which never run on a live session because it never has a world
    /// session attached) - it does not by itself push to the game; see
    /// <see cref="PushSlotAsync"/> for the entry point that does.</summary>
    bool TrySetInventorySlot(PlayerInventoryArea area, int index, InventoryItemSlot slot);

    /// <summary>
    /// Pushes one slot's CURRENT field values (as already mutated in place by the shared sidebar
    /// slot editor) to wherever this session actually lives. A no-op for the file session, which
    /// already staged the edit in place via <see cref="MarkChanged"/>; the live session sends
    /// <c>inventory.set</c> for that one slot immediately and refreshes.
    /// </summary>
    ValueTask PushSlotAsync(PlayerInventoryArea area, PlayerInventorySlotEdit slot, CancellationToken cancellationToken = default);

    /// <summary>Swaps two slots, possibly in different inventory areas (including transmog).</summary>
    ValueTask<bool> TrySwapInventorySlotsAsync(PlayerInventoryArea firstArea, int firstIndex,
        PlayerInventoryArea secondArea, int secondIndex, CancellationToken cancellationToken = default);

    ValueTask SortInventorySlotsAsync(PlayerInventoryArea area, CancellationToken cancellationToken = default);

    ValueTask<bool> TryApplyItemUpgradeAsync(PlayerInventoryArea area, int index, bool downgrade, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this session can drop an already-occupied slot straight onto the ground in the
    /// running game via <see cref="TryDropSlotLiveAsync"/>, with no separate world save required -
    /// the live counterpart to <c>InventoryTransferService.TryDropPlayerSlot</c>, which needs a
    /// file-backed world session instead (there is never one attached to a live session - see
    /// <see cref="Respawn"/>'s remarks). False for the file session, whose own DROP ITEM affordance
    /// already goes through that world session.
    /// </summary>
    bool SupportsLiveDrop => false;

    /// <summary>
    /// Drops the item already in <paramref name="index"/> onto the ground in the running game
    /// immediately (the game decides exactly where, the same as every other live drop) and clears
    /// the slot. Only meaningful when <see cref="SupportsLiveDrop"/> is true; the file session
    /// never overrides this default no-op.
    /// </summary>
    ValueTask<bool> TryDropSlotLiveAsync(PlayerInventoryArea area, int index, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);
}
