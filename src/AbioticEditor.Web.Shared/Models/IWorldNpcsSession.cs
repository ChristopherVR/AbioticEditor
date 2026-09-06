using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for narrative-NPC editing (traders/story NPCs - <c>WorldNpc</c> rows
/// with <c>IsPet == false</c>; tamed pets have their own <see cref="IWorldPetsSession"/>/
/// <c>WorldPetsTab</c>). Round 77: the file session already had this data
/// (<c>WorldSaveSession.Npcs</c>/<c>SetNpc</c>) with no dedicated tab; <c>WorldNpcsTab</c> is the
/// first UI for it, shared with a live session the same way <see cref="IWorldBasesSession"/>/
/// <see cref="IWorldVehiclesSession"/> already are.
/// </summary>
public interface IWorldNpcsSession
{
    /// <summary>Every narrative NPC known to this session (pets excluded).</summary>
    IReadOnlyList<WorldNpc> Npcs { get; }

    /// <summary>True when a mutator here takes effect in the running game immediately (live);
    /// false when it only stages an edit applied on SAVE (file).</summary>
    bool AppliesImmediately { get; }

    /// <summary>True when this process is allowed to change what it sees. Always true for a
    /// file session; reflects the running game's own host check for a live session.</summary>
    bool IsHost { get; }

    /// <summary>Freeform status from the last edit. Null for a file session; a live session
    /// uses it to say what just happened in the running game.</summary>
    string? Status { get; }

    /// <summary>
    /// Sets a narrative NPC's dead/corpse flag and narrative-state value. <paramref name="state"/>
    /// is opaque: the file session stores the game's own enum-name string
    /// (<c>E_NarrativeNPCStates::NewEnumeratorN</c>); a live session encodes its raw enum byte as
    /// a plain integer string instead (no probe has ever carried this enum's value names) - the
    /// two are never compared to each other, only round-tripped within one session.
    /// </summary>
    Task SetNpcAsync(string id, bool isDead, string? state, CancellationToken cancellationToken = default);
}
