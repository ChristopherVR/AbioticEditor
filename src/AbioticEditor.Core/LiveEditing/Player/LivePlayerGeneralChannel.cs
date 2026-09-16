namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live item and map discoveries, background, and traits. Crafted-item discovery
/// uses a host-only CraftedItems array update followed by OnRep_CraftedItems. Traits use incremental persistent buff changes without character creation rewards.
/// </summary>
public sealed class LivePlayerGeneralChannel(ILiveGameChannel channel)
{
    public LivePlayerAppearanceChannel Appearance => new(_channel);

    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveGeneralDirectory> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<DirectoryWire>("general.get", payload, cancellationToken)
            .ConfigureAwait(false);
        return new LiveGeneralDirectory(
            wire.ItemsSeen ?? [], wire.ItemsCrafted ?? [], wire.Maps ?? [], wire.Traits ?? [], wire.Background, wire.CanDiscoverCrafted, wire.CanEditTraits);
    }

    /// <summary>Discovers the given item ids as "seen", unlocks the given map ids, and/or applies
    /// a new background/PhD row name, all immediately. Crafted items and traits are not accepted
    /// - see type remarks.</summary>
    public Task SetAsync(IReadOnlyList<string>? itemsSeen = null, IReadOnlyList<string>? maps = null,
        string? background = null, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, itemsSeen, maps, background, null), cancellationToken);

    public Task DiscoverCraftedAsync(IReadOnlyList<string> ids, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("general.set", new SetWire(playerId, null, null, null, ids), cancellationToken);

    /// <summary>Changes one trait and its persistent buff without replaying character creation rewards.</summary>
    public Task SetTraitAsync(string id, bool enabled, string buffRowName, string? playerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(buffRowName);
        return _channel.RequestAsync<object?>("general.trait.set", new TraitWire(playerId, id, enabled, buffRowName), cancellationToken);
    }

    private sealed record TraitWire(string? PlayerId, string Id, bool Enabled, string BuffRowName);
    private sealed record PlayerIdWire(string PlayerId);
    private sealed record DirectoryWire(
        IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? ItemsCrafted, IReadOnlyList<string>? Maps,
        IReadOnlyList<string>? Traits, string? Background, bool CanDiscoverCrafted = false, bool CanEditTraits = false);
    private sealed record SetWire(string? PlayerId, IReadOnlyList<string>? ItemsSeen, IReadOnlyList<string>? Maps,
        string? Background, IReadOnlyList<string>? ItemsCrafted);
}

/// <summary>Item/map/trait row names the running character currently knows, plus its background.
/// Trait edits and crafted-item discovery depend on host capabilities.</summary>
public sealed record LiveGeneralDirectory(
    IReadOnlyList<string> ItemsSeen, IReadOnlyList<string> ItemsCrafted, IReadOnlyList<string> Maps,
    IReadOnlyList<string> Traits, string? Background, bool CanDiscoverCrafted = false, bool CanEditTraits = false);
