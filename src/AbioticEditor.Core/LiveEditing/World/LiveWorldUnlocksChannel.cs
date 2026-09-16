namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// World-wide unlock lists on Abiotic_Survival_GameState_C. Updated UE4SS runtimes
/// expose TSet.ForEach/Add/Remove, used for host-only world recipe editing. Runtime
/// capability reporting keeps older agents read-only.
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
            wire.IsHost, wire.CanEditRecipes, wire.GlobalRecipeEditsUnavailableReason);
    }

    /// <summary>Legacy empty request. Use SetRecipesAsync to specify edits.</summary>
    public Task SetAsync(CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set", payload: null, cancellationToken);

    public Task SetRecipesAsync(IReadOnlyList<LiveWorldRecipeEdit> recipes, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set", new { recipes }, cancellationToken);

    private sealed record UnlocksWire(
        IReadOnlyList<string>? RecipesUnlocked, IReadOnlyList<string>? RecipesResearched,
        IReadOnlyList<string>? ItemsPickedUp, IReadOnlyList<string>? EmailsRead, IReadOnlyList<string>? JournalEntries,
        IReadOnlyList<string>? CompendiumEmail, IReadOnlyList<string>? CompendiumNarrative,
        IReadOnlyList<string>? CompendiumExploration, bool IsHost, bool CanEditRecipes = false,
        string? GlobalRecipeEditsUnavailableReason = null);
}

/// <summary>
/// World-wide (not per-player) unlock lists, as read by <see cref="LiveWorldUnlocksChannel.GetAsync"/>.
/// </summary>
/// <param name="GlobalRecipeEditsUnavailableReason">Short machine-readable reason
/// <paramref name="CanEditRecipes"/> is false (null when it is true): "not-host",
/// "no-replication", or "runtime-unsupported" (an older UE4SS build without
/// TSet.Add/Remove/ForEach - see <c>areas/worldunlocks.lua</c>'s header comment).</param>
public sealed record LiveWorldUnlocks(
    IReadOnlyList<string> RecipesUnlocked, IReadOnlyList<string> RecipesResearched,
    IReadOnlyList<string> ItemsPickedUp, IReadOnlyList<string> EmailsRead, IReadOnlyList<string> JournalEntries,
    IReadOnlyList<string> CompendiumEmail, IReadOnlyList<string> CompendiumNarrative,
    IReadOnlyList<string> CompendiumExploration, bool IsHost, bool CanEditRecipes = false,
    string? GlobalRecipeEditsUnavailableReason = null);

public sealed record LiveWorldRecipeEdit(string Id, bool Unlocked);
