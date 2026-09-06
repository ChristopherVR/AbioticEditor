namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live recipe-unlock editing: reads which recipe row names the running character's
/// <c>Abiotic_CharacterProgressionComponent_C</c> already has unlocked, and unlocks more - the
/// live counterpart to the file editor's <c>PlayerRecipesTab</c> / <c>PlayerSaveReader</c>'s
/// <c>RecipesUnlock_</c> array. See <c>recipes.get</c>/<c>recipes.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/recipes.lua</c> and
/// docs/reference/live-editing-protocol.md for the wire shape and the pak evidence it is grounded
/// in (<c>tests/AbioticEditor.Probes/LiveClassPropsProbe.cs</c>, fragment
/// "CharacterProgressionComponent").
///
/// Unlike the file editor, a live recipe can only ever be unlocked, never re-locked: the
/// component's own exported function list has no lock/relock/remove-recipe function anywhere,
/// only "unlock" ones (<c>Request_UnlockNewRecipe</c>, <c>Server_TryUnlockRecipe</c>, ...) -
/// matching how the reference mod's own cheat commands only ever unlock things too.
/// </summary>
public sealed class LivePlayerRecipesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the recipe row names currently unlocked for <paramref name="playerId"/> (or
    /// the local player when omitted).</summary>
    public async Task<IReadOnlyList<string>> GetUnlockedAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<RecipesWire>("recipes.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return wire.UnlockedIds ?? [];
    }

    /// <summary>Unlocks the given recipe row names immediately. There is no live path to lock one
    /// back up - see the type-level remarks.</summary>
    public Task UnlockAsync(IReadOnlyList<string> recipeIds, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("recipes.set", new SetWire(playerId, recipeIds), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record RecipesWire(IReadOnlyList<string>? UnlockedIds);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string> UnlockIds);
}
