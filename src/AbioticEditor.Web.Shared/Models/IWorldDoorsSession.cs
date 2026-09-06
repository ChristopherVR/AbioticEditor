using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open world-doors editing session, mirroring
/// <see cref="IPlayerVitalsSession"/>'s narrow-interface pattern (see <c>PlayerVitals.cs</c>).
/// Exactly the members <c>WorldDoorsTab.razor</c> uses, extracted from <see cref="WorldSaveSession"/>'s
/// existing doors slice, so that widget binds to either the file-backed session or
/// <c>LiveDoorsSession</c> with no changes beyond its parameter's declared type.
/// </summary>
public interface IWorldDoorsSession
{
    /// <summary>Every door currently known to this session. A live session's doors carry a
    /// world position (<see cref="WorldDoor.X"/>/<see cref="WorldDoor.Y"/>/<see cref="WorldDoor.Z"/>);
    /// a file session's do not (the position is resolved separately from the game's own level
    /// files, by actor id).</summary>
    IReadOnlyList<WorldDoor> Doors { get; }

    /// <summary>Whether this save/session has any doors to edit at all.</summary>
    bool CanEditDoors { get; }

    /// <summary>Whether <see cref="Flags"/> reflects a real, editable set of story flags (used
    /// only for the "already reached/not reached that story point" hint on a story-gated door).</summary>
    bool CanEditFlags { get; }

    /// <summary>The set of story flags currently set, for the same hint. Empty when
    /// <see cref="CanEditFlags"/> is false.</summary>
    IReadOnlySet<string> Flags { get; }

    /// <summary>The loaded save's file path, used to guess which story region a heading applies
    /// to. Empty for a live session, which has no file - the per-door region guess (from the
    /// door's own sub-level) still works without it.</summary>
    string Path { get; }

    string? Status { get; }

    /// <summary>
    /// True for the live session: every mutator below takes effect in the running game
    /// immediately, there is no local "staged until Save" copy, and "keep state (no auto-reset)"
    /// has no live meaning (that flag only affects the save file's own session-restart logic).
    /// False for the file-backed session, whose mutators return an already-completed task.
    /// </summary>
    bool AppliesImmediately { get; }

    /// <summary>Whether this process may currently change doors: always true for the file-backed
    /// session (editing your own save file needs no game authority); only the game's host for the
    /// live one.</summary>
    bool IsHost { get; }

    /// <summary>Stages (file) or immediately applies (live) a stable simple-door state: Closed,
    /// Open, or Locked.</summary>
    Task SetSimpleDoorState(string id, string rawState);

    Task SetSecurityDoorOpen(string id, bool open);

    Task SetOneWayUnlocked(string id, bool unlocked);

    /// <summary>Stages the "keep state (no auto-reset)" marker. A no-op live, where it has no
    /// meaning - the tab hides the control instead of calling this while <see cref="AppliesImmediately"/>.</summary>
    Task SetDoorNoReset(string id, bool noReset);
}
