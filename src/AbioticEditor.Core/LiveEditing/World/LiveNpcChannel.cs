namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live NPC editing: lists every NPC currently loaded in the running game and lets a host toggle
/// alive/dead, disabled, invincible, and faction - see <c>npcs.list</c>/<c>npcs.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c> for the mod-side implementation,
/// built on <c>FindAllOf("NPC_Base_ParentBP_C")</c> (confirmed real, working, in a published
/// UE4SS mod for this exact game). No health or position field is exposed - none is evidenced
/// anywhere in that mod's source, so none is guessed at here either.
/// </summary>
public sealed class LiveNpcChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads every currently-loaded NPC and whether this process has host authority to
    /// edit them (NPC state is server-owned; a client's writes here would just be overwritten by
    /// replication from the real host - unlike vitals/skills, which need no such gate).</summary>
    public async Task<LiveNpcDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("npcs.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var npcs = wire.Npcs
            .Select(n => new LiveNpc(n.Id, n.Label, n.IsDead, n.IsDisabled, n.Invincible, n.Faction))
            .ToList();
        return new LiveNpcDirectory(npcs, wire.IsHost);
    }

    /// <summary>Applies edits to one or more NPCs (matched by <see cref="LiveNpc.Id"/>, re-found
    /// fresh on the mod side each call since the NPC roster changes constantly). A no-op field
    /// left null on an edit is left untouched on that NPC.</summary>
    public Task SetAsync(IReadOnlyList<LiveNpcEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("npcs.set",
            new SetWire(edits.Select(e => new EditWire(e.Id, e.IsDead, e.IsDisabled, e.Invincible, e.Faction)).ToList()),
            cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<NpcWire> Npcs, bool IsHost);
    private sealed record NpcWire(string Id, string Label, bool IsDead, bool IsDisabled, bool Invincible, int Faction);
    private sealed record SetWire(IReadOnlyList<EditWire> Npcs);
    private sealed record EditWire(string Id, bool? IsDead, bool? IsDisabled, bool? Invincible, int? Faction);
}

/// <summary>One NPC currently loaded in the running game, as listed by <see cref="LiveNpcChannel.GetAsync"/>.</summary>
/// <param name="Id">A stable id for this protocol (the game's own <c>GetFullName()</c> for this
/// specific object) - pass this back in a <see cref="LiveNpcEdit"/> to target this exact NPC.</param>
/// <param name="Label">A readable-ish label (the object's class name) - no friendlier display
/// name is exposed by the game for this actor type.</param>
public sealed record LiveNpc(string Id, string Label, bool IsDead, bool IsDisabled, bool Invincible, int Faction);

/// <summary>The full result of <see cref="LiveNpcChannel.GetAsync"/>: every loaded NPC, and
/// whether this process has host authority to edit them.</summary>
public sealed record LiveNpcDirectory(IReadOnlyList<LiveNpc> Npcs, bool IsHost);

/// <summary>One NPC edit; a null field is left untouched on that NPC.</summary>
public sealed record LiveNpcEdit(string Id, bool? IsDead = null, bool? IsDisabled = null,
    bool? Invincible = null, int? Faction = null);
