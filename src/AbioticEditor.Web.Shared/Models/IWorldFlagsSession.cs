namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open quest/story-flag editing session, implemented by
/// <see cref="WorldSaveSession"/> (flags stage in memory, written on SAVE like every other world
/// edit) and <see cref="LiveWorldFlagsSession"/> (each call applies immediately in the running
/// game through <c>flags.list</c>/<c>flags.set</c>). Same pattern as
/// <see cref="IPlayerVitalsSession"/>: one shared component (<c>WorldFlagsTab</c>) renders both.
/// Mutators are Task-returning because the live session must round-trip a TCP request to the
/// game; the file session completes them synchronously.
/// </summary>
public interface IWorldFlagsSession
{
    /// <summary>Every flag currently set: the full staged/saved set offline, the running world's
    /// live set. Not-yet-happened flags are surfaced separately by the tab from
    /// <c>QuestFlagCatalog.KnownFlags</c>, the same way for both sessions.</summary>
    IReadOnlySet<string> Flags { get; }

    /// <summary>Whether this save/connection has an editable world-flag store at all (a save
    /// missing the array, or a live world not yet loaded).</summary>
    bool CanEditFlags { get; }

    /// <summary>True only for the live session: a change here takes effect in the running game
    /// immediately instead of staging for SAVE.</summary>
    bool AppliesImmediately { get; }

    /// <summary>Whether this session is currently allowed to change flags (always true for the
    /// file session; the live session needs host authority).</summary>
    bool IsHost { get; }

    string? Status { get; }

    Task SetFlagAsync(string flag, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Adds (and sets) a raw flag name typed by hand. Returns false for a blank name.</summary>
    Task<bool> AddFlagAsync(string? flag, CancellationToken cancellationToken = default);

    /// <summary>Sets <paramref name="flag"/> and every prerequisite the story dependency graph
    /// says it needs, in one request/edit - the offer the file editor already makes rather than
    /// creating inconsistent out-of-order story state.</summary>
    Task EnableFlagWithPrerequisitesAsync(string flag, CancellationToken cancellationToken = default);

    /// <summary>Clears <paramref name="flag"/> and everything the curated dependency graph says
    /// required it, so the world never carries out-of-order story state either direction.</summary>
    Task ClearFlagWithDependentsAsync(string flag, CancellationToken cancellationToken = default);

    /// <summary>Re-reads the flag set from its source of truth. A no-op offline (the staged set
    /// is already authoritative); a fresh read of the running world live, since its own triggers
    /// can set flags at any moment.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
