namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for trader-availability editing, implemented by
/// <see cref="WorldSaveSession"/> (staged, or a direct sibling-file write for a metadata save)
/// and <see cref="LiveTradersSession"/> (immediate, against a running game). See
/// <see cref="IWorldNpcsSession"/> for the pattern this copies.
///
/// The trader roster itself (<c>TraderInfo</c> rows) is deliberately NOT part of this boundary:
/// it is static curated game data (<c>TraderCatalog</c>/<c>TraderVocabularyService</c>) that
/// <c>WorldTradersTab</c> fetches the same way regardless of which session is open - it never
/// differs between a loaded save and a running game. What differs between sessions is only which
/// quest/story flags are currently set, and how an unlock reaches storage; the tab keeps that
/// divergence (sibling-Facility-file write vs staged-flag write vs live wire write) as its own
/// type-checked branches, the same way <c>WorldContainersTab</c> branches on
/// <c>LiveContainersSession</c>/<c>WorldSaveSession</c> for genuinely different mechanics.
/// </summary>
public interface IWorldTradersSession
{
    /// <summary>True when a mutator here takes effect in the running game immediately (live);
    /// false when it only stages an edit applied on SAVE, or (for a metadata save) writes the
    /// sibling Facility save directly with its own .bak, the same as the file editor's other
    /// direct-write metadata operations.</summary>
    bool AppliesImmediately { get; }

    /// <summary>True when this process is allowed to change what it sees. Always true for a
    /// file session; reflects the running game's own host check for a live session.</summary>
    bool IsHost { get; }

    /// <summary>Freeform status from the last edit. Null for a file session; a live session
    /// uses it to say what just happened in the running game.</summary>
    string? Status { get; }

    /// <summary>
    /// Whether this session already knows about <paramref name="flag"/> as set. A metadata-save
    /// file session's own flags are not the full trader-gating picture (its trader flags live in
    /// a sibling Facility region save) - <c>WorldTradersTab</c> ORs in a separately-resolved
    /// sibling-region set for that case; a live session's answer is already the complete,
    /// current truth (see <see cref="LiveTradersSession"/>'s own remarks).
    /// </summary>
    bool HasWorldFlag(string flag);

    /// <summary>Re-reads whatever this session's flags depend on. A no-op, already-completed
    /// task for a file session (nothing to refresh - the loaded save IS the state); a live
    /// session re-reads the running game's current flags, for the REFRESH action.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
