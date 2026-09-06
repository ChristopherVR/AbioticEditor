namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open player-transmog editing session, the same narrow-interface
/// pattern <see cref="IPlayerInventorySession"/> uses for equipment/hotbar/backpack (see that
/// interface's own doc comment for why this one extends it instead of standing beside it).
/// Exactly the members <c>PlayerTransmogTab.razor</c> uses beyond the base inventory interface.
/// </summary>
public interface IPlayerTransmogSession : IPlayerInventorySession
{
    IReadOnlyList<PlayerInventorySlotEdit> Transmog { get; }

    /// <summary>
    /// The armor-visibility toggles (the first six only - see
    /// <c>docs/reference/research/research-transmog-appearance.md</c>'s "Editor guidance"). Live
    /// (round 77) writes through a real client-authoritative RPC pair on the same transmog
    /// inventory component - see <see cref="SetTransmogVisibilityAsync"/> and
    /// <c>docs/reference/live-editing-protocol.md</c>'s <c>transmog.get</c>/<c>transmog.set</c>.
    /// </summary>
    IReadOnlyList<TransmogVisibilityEdit> TransmogVisibility { get; }

    /// <summary>Applies one visibility toggle. The file session only stages the change (a plain
    /// field flip on the already-loaded <see cref="TransmogVisibility"/> edit); the live session
    /// sends <c>transmog.set</c> immediately.</summary>
    Task SetTransmogVisibilityAsync(int index, bool isVisible);
}
