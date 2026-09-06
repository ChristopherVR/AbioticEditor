namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live "bulk unlocks" editing: reads which items the running character has seen and which maps
/// it has unlocked from <c>Abiotic_CharacterProgressionComponent_C</c>, and discovers more of
/// them - the live counterpart to the file editor's General tab ITEMS SEEN and MAPS rows. See
/// <c>general.get</c>/<c>general.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/general.lua</c> and
/// docs/reference/live-editing-protocol.md for the wire shape and the pak evidence it is grounded
/// in.
///
/// ITEMS CRAFTED is read-only here: the component tracks <c>CraftedItems</c> automatically (it is
/// updated from actually crafting something, via <c>Local_CheckForNewlyCraftedItems</c>/
/// <c>OnRep_CraftedItems</c>) but exposes no single-item "mark as crafted" function anywhere in
/// its exported API, unlike items-seen (<c>Server_CheckNewItemPickedUp</c>) and maps
/// (<c>Server_AddMapToJournal</c>). The account/owner-id change and the file editor's own
/// vocabulary-driven "discover ALL at once" bulk action have no live equivalent at all - this
/// channel only offers per-id discovery, called once per id from the web host's live general
/// session for a "discover all" action.
/// </summary>
public sealed class LivePlayerGeneralChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveGeneralDirectory> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<DirectoryWire>("general.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return new LiveGeneralDirectory(wire.ItemsSeen ?? [], wire.ItemsCrafted ?? [], wire.Maps ?? []);
    }

    /// <summary>Discovers the given item ids as "seen" and/or unlocks the given map ids
    /// immediately. Crafted items are not accepted - see type remarks.</summary>
    public Task SetAsync(IReadOnlyList<string>? itemsSeen = null, IReadOnlyList<string>? maps = null,
        string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, itemsSeen, maps), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record DirectoryWire(
        IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? ItemsCrafted, IReadOnlyList<string>? Maps);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? Maps);
}

/// <summary>Item/map row names the running character currently knows. <see cref="ItemsCrafted"/>
/// is read-only - see <see cref="LivePlayerGeneralChannel"/>'s remarks.</summary>
public sealed record LiveGeneralDirectory(
    IReadOnlyList<string> ItemsSeen, IReadOnlyList<string> ItemsCrafted, IReadOnlyList<string> Maps);
