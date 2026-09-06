namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for editing where a player respawns, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>):
/// exactly the members <c>PlayerSpawnTab.razor</c> needs, extracted from
/// <see cref="PlayerSaveSession"/>'s existing <see cref="PlayerSaveSession.Respawn"/> slice, so
/// that widget binds to either the file-backed session or <see cref="LivePlayerSpawnSession"/>
/// with only its parameter's declared type changing.
///
/// Unlike vitals, a live connection has no file to stage a new respawn point into: moving the
/// character is a real, immediate teleport, and claiming a terminal is a direct field write with
/// no "unsaved until SAVE" concept. The capability flags below let the shared tab hide or add
/// whatever a given session can or cannot do, instead of the tab silently misbehaving.
/// </summary>
public interface IPlayerSpawnSession
{
    /// <summary>The respawn point being edited: coordinates plus which respawn terminal is
    /// claimed. For the live session, editing these fields alone never moves anyone - only the
    /// explicit actions gated by <see cref="SupportsLiveActions"/> do.</summary>
    PlayerRespawnEdit Respawn { get; }

    /// <summary>A stable key that changes only when the save/connection this tab is bound to
    /// actually changes (a different player file, a different connected player) - the file
    /// session's own <see cref="PlayerSaveSession.Path"/> for the file case; a live session has no
    /// path, so it uses the target player's id instead.</summary>
    string SessionKey { get; }

    /// <summary>True for the file session: the region and bed pickers need a save file on disk
    /// and (for beds) a sibling world save on disk, neither of which a live connection has.</summary>
    bool SupportsWorldIntegration { get; }

    /// <summary>True for the live session: renders the extra TELEPORT / SET AS MY RESPAWN POINT
    /// buttons, each one explicit and immediate - never automatic (see
    /// <c>docs/reference/live-editing-protocol.md</c>).</summary>
    bool SupportsLiveActions { get; }

    /// <summary>Live only: the connected character's actual current position, shown for reference
    /// and as the seed for "teleport here". Null for the file session - a save has no "right now",
    /// only whatever respawn point it last stored.</summary>
    (double X, double Y, double Z)? LivePosition { get; }

    bool IsDirty { get; }
    string? Status { get; }
    void MarkChanged();
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();
}
