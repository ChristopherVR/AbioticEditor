namespace AbioticEditor.Web.Models;

/// <summary>
/// Composite boundary for <c>PlayerEditor.razor</c>'s own <c>Session</c> parameter: every narrow
/// per-area interface its child tabs already bind to, combined into one type so the SAME
/// component (tab strip, viewport, palette wiring) can host either the file-backed
/// <see cref="PlayerSaveSession"/> (which already implements all of these) or a live connection
/// (see <c>LivePlayerEditorSession</c>), instead of live editing hand-rolling its own separate
/// flat tab strip - the reuse this interface exists to enable is exactly what the file editor and
/// live editing had been duplicating before it.
///
/// Deliberately adds no members of its own: CHARACTER/ACHIEVEMENTS/DATA have no live equivalent
/// at all (a save file has no "character customization" or "raw JSON tree" concept once the
/// character is already in a running game), so <c>PlayerEditor.razor</c> gates those three tabs
/// on the concrete <see cref="PlayerSaveSession"/> type directly rather than growing this
/// interface with capability flags nothing else would ever implement differently.
/// </summary>
public interface IPlayerEditorSession :
    IPlayerVitalsSession,
    IPlayerSkillsSession,
    IPlayerTransmogSession,
    IPlayerSpawnSession,
    IPlayerCompanionsSession,
    IPlayerRecipesSession,
    IPlayerCodexSession,
    IPlayerGeneralSession
{
    // IPlayerVitalsSession and IPlayerInventorySession (via IPlayerTransmogSession) each declare
    // an identically-shaped `Vitals` member of their own - one implementation always satisfies
    // both (see PlayerSaveSession/LivePlayerEditorSession), but C# still treats a reference typed
    // as this composite interface as ambiguous between the two inherited declarations unless the
    // composite redeclares the member itself to unify them. `PlayerEditor.razor` reads
    // `Session.Vitals` directly (for the VITALS tab), which is what surfaces the ambiguity - no
    // other member here is actually read through this composite type the same way, so none of the
    // other same-shaped duplicates across these eight interfaces (IsDirty/Status/SaveAsync/Revert,
    // SessionKey/SupportsWorldIntegration, MarkChanged, ...) need the same treatment.
    new PlayerVitals Vitals { get; }
}
