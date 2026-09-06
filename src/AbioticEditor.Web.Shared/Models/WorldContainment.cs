using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for editing Leyak Containment Units, implemented by the file session
/// (<see cref="WorldSaveSession"/>, a folder-wide survey of every region save's units) and the
/// live session (<see cref="LiveContainmentSession"/>, a direct read of the game's own
/// <c>Deployed_LeyakContainment_C</c> actors). <c>WorldContainmentTab</c> binds to this interface
/// alone, so the same widget renders both a loaded save and a running game - see
/// <c>IPlayerVitalsSession</c>/<c>PlayerVitalsTab</c> for the precedent this follows.
/// </summary>
public interface IWorldContainmentSession
{
    /// <summary>True once the unit list has been read at least once.</summary>
    bool ContainmentUnitsLoaded { get; }

    /// <summary>Every containment unit known right now.</summary>
    IReadOnlyList<WorldContainmentUnit> ContainmentUnits { get; }

    /// <summary>Region saves (file session) or live areas (none, for a live session) that could
    /// not be read, so the list may be incomplete.</summary>
    IReadOnlyList<string> ContainmentScanFailures { get; }

    /// <summary>Creature row -> unit id entries, matching whichever units are currently occupied.</summary>
    IReadOnlyList<KeyValuePair<string, string>> Containments { get; }

    /// <summary>Creature -> unit id entries pointing at a unit that no longer exists.</summary>
    IReadOnlyList<KeyValuePair<string, string>> OrphanedContainments { get; }

    /// <summary>
    /// True when an edit takes effect immediately (live: the real game changes as soon as the
    /// call returns) rather than staging until the file session's own SAVE.
    /// </summary>
    bool AppliesImmediately { get; }

    /// <summary>(Re-)reads the unit list. Safe to call repeatedly; a file session caches its
    /// (expensive, multi-file) survey, a live session re-reads the running game every time.</summary>
    Task LoadContainmentUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>The creature currently assigned to <paramref name="unitId"/>, or null when empty.</summary>
    string? CreatureInUnit(string unitId);

    /// <summary>Assigns <paramref name="creature"/> (or null to empty it) into <paramref name="unitId"/>.</summary>
    Task SetContainmentUnitOccupantAsync(string unitId, string? creature, CancellationToken cancellationToken = default);

    /// <summary>Exchanges the occupants of two units in one step.</summary>
    Task SwapContainmentUnitsAsync(string unitIdA, string unitIdB, CancellationToken cancellationToken = default);

    /// <summary>Frees <paramref name="creature"/> from whichever unit currently holds it.</summary>
    Task ReleaseContainmentAsync(string creature, CancellationToken cancellationToken = default);
}
