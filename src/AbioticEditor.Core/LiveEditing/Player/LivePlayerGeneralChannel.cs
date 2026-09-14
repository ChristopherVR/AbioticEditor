namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live item and map discoveries, background, and trait readout. Crafted-item discovery
/// uses a host-only CraftedItems array update followed by OnRep_CraftedItems. Traits remain
/// read-only until their gameplay side effects have a verified update path.
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
            wire.ItemsSeen ?? [], wire.ItemsCrafted ?? [], wire.Maps ?? [], wire.Traits ?? [], wire.Background, wire.CanDiscoverCrafted);
    }

    /// <summary>Discovers the given item ids as "seen", unlocks the given map ids, and/or applies
    /// a new background/PhD row name, all immediately. Crafted items and traits are not accepted
    /// - see type remarks.</summary>
    public Task SetAsync(IReadOnlyList<string>? itemsSeen = null, IReadOnlyList<string>? maps = null,
        string? background = null, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, itemsSeen, maps, background, null), cancellationToken);

    public Task DiscoverCraftedAsync(IReadOnlyList<string> ids, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, null, null, null, ids), cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record DirectoryWire(
        IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? ItemsCrafted, IReadOnlyList<string>? Maps,
        IReadOnlyList<string>? Traits, string? Background, bool CanDiscoverCrafted = false);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? Maps,
        string? Background, IReadOnlyList<string>? ItemsCrafted);
}

/// <summary>Item/map/trait row names the running character currently knows, plus its background.
/// Traits are read-only; crafted-item discovery depends on the host capability.</summary>
public sealed record LiveGeneralDirectory(
    IReadOnlyList<string> ItemsSeen, IReadOnlyList<string> ItemsCrafted, IReadOnlyList<string> Maps,
    IReadOnlyList<string> Traits, string? Background, bool CanDiscoverCrafted = false);
