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
    /// The armor-visibility toggles. A live session has no confirmed property to write these
    /// through (see <c>docs/reference/live-editing-protocol.md</c>), so it reports the six roles
    /// as read-only; the tab checks <see cref="IPlayerInventorySession.AppliesImmediately"/> and
    /// renders them disabled with a note there rather than letting an edit silently not apply.
    /// </summary>
    IReadOnlyList<TransmogVisibilityEdit> TransmogVisibility { get; }
}
