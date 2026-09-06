using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open PETS editing session, implemented by
/// <see cref="WorldSaveSession"/> (staged, always available) and <see cref="LivePetsSession"/>
/// (against a running game - see that class for why it reports <see cref="IsAvailable"/> false
/// today: the game exposes tame/name/health data inconsistently between creature families, with
/// no safe way to match a live actor back to this file's pet records). See
/// <see cref="IWorldBasesSession"/> for the pattern this copies.
/// </summary>
public interface IWorldPetsSession
{
    /// <summary>Every pet known to this session (staged edits included, for the file session);
    /// empty when <see cref="IsAvailable"/> is false.</summary>
    IReadOnlyList<WorldPet> Pets { get; }

    /// <summary>True when a mutator here takes effect in the running game immediately (live).</summary>
    bool AppliesImmediately { get; }

    /// <summary>False when this session has no working pet data at all (see class remarks on
    /// <see cref="LivePetsSession"/>); the tab shows <see cref="UnavailableReason"/> instead of
    /// an empty list, so it reads as "not supported" rather than "no pets here".</summary>
    bool IsAvailable { get; }

    /// <summary>Player-safe explanation shown by the tab when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>False when this session has no way to change a pet's species/creature type (a
    /// live session never does - see <see cref="LivePetsSession"/>'s remarks). The tab hides the
    /// creature-type dropdown when this is false.</summary>
    bool SupportsSpeciesChange { get; }

    /// <summary>False when this session has no way to remove a pet (a live session never does -
    /// no despawn/respawn round trip has any precedent for a living NPC). The tab hides the
    /// delete/undo-delete controls when this is false.</summary>
    bool SupportsRemoval { get; }

    /// <summary>Stages/applies a pet's persisted fields.</summary>
    Task SetPetAsync(string id, bool isDead, string? npcClass, string? customName, int xp,
        IReadOnlyDictionary<string, double> limbHealth, CancellationToken cancellationToken = default);

    /// <summary>Removes a pet.</summary>
    Task RemovePetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Un-does a pending removal (the UNDO DELETE action). Returns true if restored.</summary>
    Task<bool> RestorePetAsync(WorldPet pet, CancellationToken cancellationToken = default);
}
