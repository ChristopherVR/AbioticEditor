namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live narrative-NPC editing: lists every story NPC/trader currently loaded
/// (<c>NarrativeNPC_ParentBP</c> and its subclasses) with its corpse/narrative-state flags, and
/// lets a host change them - see <c>narrativenpcs.list</c>/<c>narrativenpcs.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/narrative.lua</c>. Round 77: the offline
/// session already had <c>WorldSaveSession.Npcs</c>/<c>SetNpc</c> for narrative NPCs with no
/// dedicated tab; this is the live counterpart, shared through <c>IWorldNpcsSession</c>/
/// <c>WorldNpcsTab</c>.
/// </summary>
public sealed class LiveNarrativeNpcsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveNarrativeNpcDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("narrativenpcs.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var npcs = (wire.Npcs ?? [])
            .Select(n => new LiveNarrativeNpc(n.Id, n.Label, n.IsCorpse, n.NarrativeState, n.X, n.Y, n.Z))
            .ToList();
        return new LiveNarrativeNpcDirectory(npcs, wire.IsHost);
    }

    /// <summary>Applies edits immediately. Host only.</summary>
    public Task SetAsync(IReadOnlyList<LiveNarrativeNpcEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("narrativenpcs.set",
            new SetWire(edits.Select(e => new EditWire(e.Id, e.IsCorpse, e.NarrativeState)).ToList()), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<NpcWire>? Npcs, bool IsHost);
    private sealed record NpcWire(string Id, string Label, bool IsCorpse, int NarrativeState, double X, double Y, double Z);
    private sealed record SetWire(IReadOnlyList<EditWire> Npcs);
    private sealed record EditWire(string Id, bool? IsCorpse, int? NarrativeState);
}

/// <summary>One loaded narrative NPC. <paramref name="Id"/> is the game's own full object name
/// for this exact actor. <paramref name="NarrativeState"/> is the raw enum byte, not the file's
/// own string encoding - see the Lua module's own comment for why.</summary>
public sealed record LiveNarrativeNpc(string Id, string Label, bool IsCorpse, int NarrativeState,
    double X, double Y, double Z);

/// <summary>One edit; a null field is left untouched.</summary>
public sealed record LiveNarrativeNpcEdit(string Id, bool? IsCorpse = null, int? NarrativeState = null);

/// <summary>Every loaded narrative NPC plus whether this process has host authority to change them.</summary>
public sealed record LiveNarrativeNpcDirectory(IReadOnlyList<LiveNarrativeNpc> Npcs, bool IsHost);
