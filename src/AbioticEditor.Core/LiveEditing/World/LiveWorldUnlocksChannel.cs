namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live WORLD-LEVEL (not per-player) unlock lists: <c>GlobalRecipesUnlocked</c>,
/// <c>GlobalRecipesResearched</c>, <c>GlobalItemsPickedUp</c>, <c>GlobalEmailsRead</c>,
/// <c>GlobalJournalEntries</c> and <c>GlobalCompendiumEmail</c>/<c>Narrative</c>/<c>Exploration</c>
/// on <c>Abiotic_Survival_GameState_C</c> - the live counterpart to the file editor's world-recipes
/// browser (<c>WorldSaveSession.GlobalRecipes</c>, the save's <c>GlobalUnlocks</c> struct). See
/// <c>worldunlocks.get</c>/<c>worldunlocks.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/worldunlocks.lua</c> for the grounding
/// evidence and exactly why there is no write path: no unlock function exists anywhere in the
/// game's own exported API for any of these fields, and directly mutating a replicated
/// <c>TSet</c>/<c>TArray</c> property has no confirmed technique in this project or any installed
/// mod. <see cref="SetAsync"/> therefore always throws - it exists only so a future grounded write
/// path has somewhere to plug in without a wire-shape change.
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
            wire.IsHost);
    }

    /// <summary>Always throws - see the type remarks for exactly why no write path is grounded.</summary>
    public Task SetAsync(CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("worldunlocks.set", payload: null, cancellationToken);

    private sealed record UnlocksWire(
        IReadOnlyList<string>? RecipesUnlocked, IReadOnlyList<string>? RecipesResearched,
        IReadOnlyList<string>? ItemsPickedUp, IReadOnlyList<string>? EmailsRead, IReadOnlyList<string>? JournalEntries,
        IReadOnlyList<string>? CompendiumEmail, IReadOnlyList<string>? CompendiumNarrative,
        IReadOnlyList<string>? CompendiumExploration, bool IsHost);
}

/// <summary>World-wide (not per-player) unlock lists, as read by <see cref="LiveWorldUnlocksChannel.GetAsync"/>.</summary>
public sealed record LiveWorldUnlocks(
    IReadOnlyList<string> RecipesUnlocked, IReadOnlyList<string> RecipesResearched,
    IReadOnlyList<string> ItemsPickedUp, IReadOnlyList<string> EmailsRead, IReadOnlyList<string> JournalEntries,
    IReadOnlyList<string> CompendiumEmail, IReadOnlyList<string> CompendiumNarrative,
    IReadOnlyList<string> CompendiumExploration, bool IsHost);
