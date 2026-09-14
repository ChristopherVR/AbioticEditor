namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live recipe reads and unlock RPCs. An updated host agent can also remove recipe names
/// from RecipesUnlockedArray and invoke OnRep_RecipesUnlockedArray. Clients and older
/// agents advertise CanLock=false. See the live protocol for the verified field/API sources.
/// </summary>
public sealed class LivePlayerRecipesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads the recipe row names currently unlocked for <paramref name="playerId"/> (or
    /// the local player when omitted).</summary>
    public async Task<IReadOnlyList<string>> GetUnlockedAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
        => (await GetAsync(playerId, cancellationToken).ConfigureAwait(false)).UnlockedIds;

    public async Task<LiveRecipeDirectory> GetAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<RecipesWire>("recipes.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return new LiveRecipeDirectory(wire.UnlockedIds ?? [], wire.CanLock);
    }

    /// <summary>Unlocks recipe rows through the running game.</summary>
    public Task UnlockAsync(IReadOnlyList<string> recipeIds, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("recipes.set", new SetWire(playerId, recipeIds, null), cancellationToken);

    public Task LockAsync(IReadOnlyList<string> recipeIds, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("recipes.set", new SetWire(playerId, null, recipeIds), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record RecipesWire(IReadOnlyList<string>? UnlockedIds, bool CanLock = false);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string>? UnlockIds, IReadOnlyList<string>? LockIds);
}

public sealed record LiveRecipeDirectory(IReadOnlyList<string> UnlockedIds, bool CanLock);
