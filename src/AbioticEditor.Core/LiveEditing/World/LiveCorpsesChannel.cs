namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live corpse editing: the "Corpses" feature's live twin (the same actors
/// <c>Core/WorldSaves/Features/CorpseMapFeature.cs</c> edits in the save's <c>CorpseMap</c>). Lists
/// every loaded corpse actor found by a hierarchy sweep of <c>CharacterCorpse_ParentBP_C</c> (every
/// current and future subclass, no class name hardcoded on this side or the Lua side - see
/// <c>corpses.list</c>/<c>corpses.remove</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/corpses.lua</c> for how that discovery
/// works) and lets a host remove one.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump of
/// <c>CharacterCorpse_ParentBP_C</c>: <see cref="LiveCorpse.Gibbed"/>/<see cref="LiveCorpse.Looted"/>
/// map to the real live properties <c>actor.IsGibbed</c> (replicated, RepNotify
/// <c>OnRep_IsGibbed</c>) and <c>actor.HasBeenLooted</c> (replicated, no RepNotify) - one field name
/// apart from the save leaves (<c>IsGibbed_</c>/<c>IsLooted_</c>) the file editor reads, same
/// meaning. Both stay read-only here too, matching <c>CorpseMapFeature.cs</c>'s own reasoning ("no
/// in-game reason to flip either by hand"); both fields are nullable because a specific actor can
/// still fail to read right now, not because either is ever guessed.</para>
///
/// <para><b>Removal, confirmed not guessed:</b> no function on <c>CharacterCorpse_ParentBP_C</c>
/// cleanly despawns an already-placed corpse (checked every declared function against the dump -
/// see the Lua module's own header comment), matching round 78's identical finding for tamed pets,
/// so this uses the same standard <c>K2_DestroyActor()</c> the reference CheatConsoleCommands mod's
/// own "deleteobject" command already uses on an arbitrary world actor. No undo once this
/// returns.</para>
/// </summary>
public sealed class LiveCorpsesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveCorpseDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("corpses.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var corpses = (wire.Corpses ?? [])
            .Select(c => new LiveCorpse(c.Id, c.Label, c.Gibbed, c.Looted, c.X, c.Y, c.Z))
            .ToList();
        return new LiveCorpseDirectory(corpses, wire.IsHost);
    }

    /// <summary>Removes a corpse by destroying its live actor outright (see the class remarks: no
    /// blueprint function cleanly despawns one, so this uses the same standard
    /// <c>K2_DestroyActor</c> call <c>pets.remove</c> already uses). Host only. There is no undo
    /// once this returns.</summary>
    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("corpses.remove", new IdWire(id), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<CorpseWire>? Corpses, bool IsHost);
    private sealed record CorpseWire(string Id, string Label, bool? Gibbed, bool? Looted, double X, double Y, double Z);
    private sealed record IdWire(string Id);
}

/// <summary>One loaded corpse actor. <paramref name="Id"/> is the game's full object name for this
/// exact actor; <paramref name="Gibbed"/>/<paramref name="Looted"/> are null when no live property
/// could be located for them right now (see the class remarks) rather than a guessed false.</summary>
public sealed record LiveCorpse(string Id, string Label, bool? Gibbed, bool? Looted, double X, double Y, double Z);

/// <summary>Every loaded corpse plus whether this process has host authority to remove them.</summary>
public sealed record LiveCorpseDirectory(IReadOnlyList<LiveCorpse> Corpses, bool IsHost);
