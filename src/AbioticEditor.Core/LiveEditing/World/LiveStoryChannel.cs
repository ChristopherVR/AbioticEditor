namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live main-quest indicator and setter: reads the running game's replicated
/// <c>CurrentQuest</c> row off <c>Abiotic_Survival_GameState_C</c>, and moves the story chapter by
/// setting/clearing the same world flags <see cref="LiveWorldFlagsChannel"/> drives - see
/// <c>story.get</c>/<c>story.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/story.lua</c> for the exact evidence (the
/// PDB's <c>UWorldFlagSubsystem::FindCurrentQuest</c>/<c>SetWorldFlag</c> and the blueprint's
/// <c>CurrentQuest</c>/<c>OnRep_CurrentQuest</c>). The story chapter is a function of world
/// flags (<c>StoryProgressionCatalog</c>/<c>FlagGate</c>), so the caller (<c>LiveStorySession</c>
/// in the Razor host, which is where those catalogs live) computes the flag lists and this
/// channel only carries them across the wire.
/// </summary>
public sealed class LiveStoryChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveStoryState> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<StateWire>("story.get", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveStoryState(
            string.IsNullOrEmpty(wire.CurrentQuestRow) ? "None" : wire.CurrentQuestRow, wire.IsHost);
    }

    /// <summary>
    /// Moves the story chapter to <paramref name="targetQuestRow"/> by setting/clearing the flags
    /// the caller already computed, then nudging the replicated <c>CurrentQuest</c> row (best
    /// effort - see the Lua module's header comment). The flags are the real, game-native write;
    /// the row nudge is a belt-and-braces extra. Returns the flag names the game did not know
    /// and left alone (see <see cref="LiveWorldFlagsChannel.SetAsync"/>); empty when all applied.
    /// </summary>
    public async Task<IReadOnlyList<string>> SetAsync(
        string targetQuestRow, IReadOnlyList<string> flagsToSet, IReadOnlyList<string> flagsToClear,
        CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<SetResultWire?>(
            "story.set", new SetWire(targetQuestRow, flagsToSet, flagsToClear), cancellationToken).ConfigureAwait(false);
        return wire?.Skipped ?? [];
    }

    private sealed record StateWire(string? CurrentQuestRow, bool IsHost);
    private sealed record SetWire(string CurrentQuestRow, IReadOnlyList<string> FlagsToSet, IReadOnlyList<string> FlagsToClear);
    private sealed record SetResultWire(IReadOnlyList<string>? Skipped);
}

/// <summary>The running game's current-quest row name, as read by <see cref="LiveStoryChannel.GetAsync"/>.
/// <c>None</c> when the game reports no active quest, or a row name outside anything
/// <c>StoryProgressionCatalog</c> knows - the shared story tab renders that as "unknown chapter"
/// exactly like an unrecognised save value.</summary>
public sealed record LiveStoryState(string CurrentQuestRow, bool IsHost);
