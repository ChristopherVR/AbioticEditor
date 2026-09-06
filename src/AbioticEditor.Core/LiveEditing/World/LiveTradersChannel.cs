namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live trader availability: no mod anywhere touches a trader UObject directly (the barter
/// mechanics are pure UI/data-table driven), but trader/stock gating IS a set of quest/story
/// world flags (<c>Core/Catalogs/Codex/TraderCatalog.cs</c>'s <c>RequiredFlags</c>/<c>RequiredFlag</c>),
/// the same <c>UWorldFlagSubsystem</c> <see cref="LiveWorldFlagsChannel"/> already drives. This
/// channel is a thin, intent-named wrapper over that same live write path - see
/// <c>traders.list</c>/<c>traders.unlock</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/traders.lua</c>.
/// </summary>
public sealed class LiveTradersChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Every quest/story flag currently set in the running game, plus host authority.</summary>
    public async Task<LiveTraderFlags> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<ListWire>("traders.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveTraderFlags(new HashSet<string>(wire.SetFlags ?? [], StringComparer.OrdinalIgnoreCase), wire.IsHost);
    }

    /// <summary>Sets every flag in <paramref name="flags"/> through the game's own world-flag
    /// subsystem, so dependent trader stock/appearance reacts exactly as if earned in play.</summary>
    public Task UnlockAsync(IReadOnlyCollection<string> flags, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("traders.unlock", new UnlockWire(flags.ToList()), cancellationToken);

    private sealed record ListWire(IReadOnlyList<string>? SetFlags, bool IsHost);
    private sealed record UnlockWire(IReadOnlyList<string> Flags);
}

/// <summary>The running game's currently-set quest/story flags, for trader gating, plus whether
/// this process has host authority to change them.</summary>
public sealed record LiveTraderFlags(IReadOnlySet<string> SetFlags, bool IsHost)
{
    public bool HasFlag(string flag) => SetFlags.Contains(flag);
}
