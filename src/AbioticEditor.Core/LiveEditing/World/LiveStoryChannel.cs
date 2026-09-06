namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live main-quest indicator: reads the running game's replicated <c>CurrentQuest</c> row off
/// <c>Abiotic_Survival_GameState_C</c> - see <c>story.get</c>/<c>story.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/story.lua</c> for the exact evidence
/// (the PDB's <c>UWorldFlagSubsystem::FindCurrentQuest</c> and the blueprint's
/// <c>CurrentQuest</c>/<c>OnRep_CurrentQuest</c>). There is no grounded live write path for the
/// story chapter itself (no native setter exists), so this channel is read-only: <c>story.set</c>
/// always fails, and the Razor host's <c>LiveStorySession</c> never calls it.
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

    private sealed record StateWire(string? CurrentQuestRow, bool IsHost);
}

/// <summary>The running game's current-quest row name, as read by <see cref="LiveStoryChannel.GetAsync"/>.
/// <c>None</c> when the game reports no active quest, or a row name outside anything
/// <c>StoryProgressionCatalog</c> knows - the shared story tab renders that as "unknown chapter"
/// exactly like an unrecognised save value.</summary>
public sealed record LiveStoryState(string CurrentQuestRow, bool IsHost);
