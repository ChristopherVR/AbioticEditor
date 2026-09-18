namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// World-wide unlock lists on Abiotic_Survival_GameState_C. Updated UE4SS runtimes
/// expose TSet.ForEach/Add/Remove, used for host-only world recipe editing. Runtime
/// capability reporting keeps older agents read-only.
///
/// Round 106: the six FArrayProperty lists (<see cref="LiveWorldUnlocks.ItemsPickedUp"/>,
/// <see cref="LiveWorldUnlocks.EmailsRead"/>, <see cref="LiveWorldUnlocks.JournalEntries"/>,
/// <see cref="LiveWorldUnlocks.CompendiumEmail"/>, <see cref="LiveWorldUnlocks.CompendiumNarrative"/>,
/// <see cref="LiveWorldUnlocks.CompendiumExploration"/>) are settable too, through
/// <see cref="SetGlobalListAsync"/>. None of them carries a "Net"/RepNotify flag in the pak dump
/// (nor do the two recipe TSets beside them), so writes use plain array replacement (the same
/// technique <c>codex.lua</c>'s per-player array clear already uses) plus a best-effort
/// replication-dirty mark, gated by <see cref="LiveWorldUnlocks.CanEditGlobalLists"/> - host
/// authority and replication-notification support only, no TSet capability needed (unlike
/// <see cref="LiveWorldUnlocks.CanEditRecipes"/>) - see <c>areas/worldunlocks.lua</c>'s header
/// comment for the full evidence.
/// </summary>
public sealed class LiveWorldUnlocksChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveWorldUnlocks> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<UnlocksWire>("worldunlocks.get", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveWorldUnlocks(
            wire.RecipesUnlocked ?? [], wire.RecipesResearched ?? [], wire.ItemsPickedUp ?? [],
            wire.EmailsRead ?? [], wire.JournalEntries ?? [],
            wire.CompendiumEmail ?? [], wire.CompendiumNarrative ?? [], wire.CompendiumExploration ?? [],
            wire.IsHost, wire.CanEditRecipes, wire.GlobalRecipeEditsUnavailableReason,
            wire.CanEditGlobalLists, wire.GlobalListEditsUnavailableReason);
    }

    /// <summary>Legacy empty request. Use SetRecipesAsync to specify edits.</summary>
    public Task SetAsync(CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set", payload: null, cancellationToken);

    public Task SetRecipesAsync(IReadOnlyList<LiveWorldRecipeEdit> recipes, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set", new { recipes }, cancellationToken);

    /// <summary>Round 106: adds/removes rows in one of the six world-wide FArrayProperty lists -
    /// see this class's remarks. <paramref name="list"/> is the wire field name
    /// (<c>"itemsPickedUp"</c>, <c>"emailsRead"</c>, <c>"journalEntries"</c>,
    /// <c>"compendiumEmail"</c>, <c>"compendiumNarrative"</c>, or
    /// <c>"compendiumExploration"</c>).</summary>
    public Task SetGlobalListAsync(string list, IReadOnlyList<LiveWorldListEdit> edits, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set",
            new Dictionary<string, object?> { [list] = edits }, cancellationToken);

    private sealed record UnlocksWire(
        IReadOnlyList<string>? RecipesUnlocked, IReadOnlyList<string>? RecipesResearched,
        IReadOnlyList<string>? ItemsPickedUp, IReadOnlyList<string>? EmailsRead, IReadOnlyList<string>? JournalEntries,
        IReadOnlyList<string>? CompendiumEmail, IReadOnlyList<string>? CompendiumNarrative,
        IReadOnlyList<string>? CompendiumExploration, bool IsHost, bool CanEditRecipes = false,
        string? GlobalRecipeEditsUnavailableReason = null, bool CanEditGlobalLists = false,
        string? GlobalListEditsUnavailableReason = null);
}

/// <summary>
/// World-wide (not per-player) unlock lists, as read by <see cref="LiveWorldUnlocksChannel.GetAsync"/>.
/// </summary>
/// <param name="GlobalRecipeEditsUnavailableReason">Short machine-readable reason
/// <paramref name="CanEditRecipes"/> is false (null when it is true): "not-host",
/// "no-replication", or "runtime-unsupported" (an older UE4SS build without
/// TSet.Add/Remove/ForEach - see <c>areas/worldunlocks.lua</c>'s header comment).</param>
/// <param name="CanEditGlobalLists">Round 106: whether <see cref="ItemsPickedUp"/>/
/// <see cref="EmailsRead"/>/<see cref="JournalEntries"/>/<see cref="CompendiumEmail"/>/
/// <see cref="CompendiumNarrative"/>/<see cref="CompendiumExploration"/> can be edited through
/// <see cref="LiveWorldUnlocksChannel.SetGlobalListAsync"/>: host authority and
/// replication-notification support, same as recipes but without the extra TSet-capability
/// check (these are plain arrays).</param>
/// <param name="GlobalListEditsUnavailableReason">Short machine-readable reason
/// <paramref name="CanEditGlobalLists"/> is false: "not-host" or "no-replication" (never
/// "runtime-unsupported" - array assignment needs no TSet support).</param>
public sealed record LiveWorldUnlocks(
    IReadOnlyList<string> RecipesUnlocked, IReadOnlyList<string> RecipesResearched,
    IReadOnlyList<string> ItemsPickedUp, IReadOnlyList<string> EmailsRead, IReadOnlyList<string> JournalEntries,
    IReadOnlyList<string> CompendiumEmail, IReadOnlyList<string> CompendiumNarrative,
    IReadOnlyList<string> CompendiumExploration, bool IsHost, bool CanEditRecipes = false,
    string? GlobalRecipeEditsUnavailableReason = null, bool CanEditGlobalLists = false,
    string? GlobalListEditsUnavailableReason = null);

public sealed record LiveWorldRecipeEdit(string Id, bool Unlocked);

/// <summary>One row to add or remove from a world-wide list - see
/// <see cref="LiveWorldUnlocksChannel.SetGlobalListAsync"/>.</summary>
public sealed record LiveWorldListEdit(string Id, bool Present);
