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
///
/// BACKGROUND (round 77) is a real write: <c>Abiotic_PlayerState_C.PhD</c> is a plain, no-hash
/// FName property with no <c>OnRep_PhD</c>, so this writes it directly on the server's own
/// authoritative PlayerState object (a replicated UPROPERTY changed there reaches owning clients
/// on the next network update, no RPC needed). TRAITS stays read-only: the only functions that
/// touch <c>CharacterProgressionComponent.Traits</c> are unprefixed local functions used solely by
/// the one-time character-creation flow, and the native engine's only trait-adjacent RPCs
/// (<c>UCharacterBuffComponent::Server_AddTraitBuff</c>/<c>Server_RemoveTraitBuff</c>) apply a
/// different, temporary buff effect rather than editing this list - see
/// <c>general.lua</c>'s header comment for the full evidence trail.
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
        return new LiveGeneralDirectory(
            wire.ItemsSeen ?? [], wire.ItemsCrafted ?? [], wire.Maps ?? [], wire.Traits ?? [], wire.Background);
    }

    /// <summary>Discovers the given item ids as "seen", unlocks the given map ids, and/or applies
    /// a new background/PhD row name, all immediately. Crafted items and traits are not accepted
    /// - see type remarks.</summary>
    public Task SetAsync(IReadOnlyList<string>? itemsSeen = null, IReadOnlyList<string>? maps = null,
        string? background = null, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, itemsSeen, maps, background), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record DirectoryWire(
        IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? ItemsCrafted, IReadOnlyList<string>? Maps,
        IReadOnlyList<string>? Traits, string? Background);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? Maps,
        string? Background);
}

/// <summary>Item/map/trait row names the running character currently knows, plus its background.
/// <see cref="ItemsCrafted"/> and <see cref="Traits"/> are read-only - see
/// <see cref="LivePlayerGeneralChannel"/>'s remarks.</summary>
public sealed record LiveGeneralDirectory(
    IReadOnlyList<string> ItemsSeen, IReadOnlyList<string> ItemsCrafted, IReadOnlyList<string> Maps,
    IReadOnlyList<string> Traits, string? Background);
