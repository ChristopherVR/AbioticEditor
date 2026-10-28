namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live Community Update 5 multi-quest support: reads which quest line the running character
/// tracks and which objectives the game reports active, and changes the tracked line. See
/// <c>quests.get</c>/<c>quests.track</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/quests.lua</c> for the grounding.
/// Tracking belongs to the local player's controller, so another player's state is read-only.
/// </summary>
public sealed class LiveQuestsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveQuestState> GetAsync(string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<StateWire>("quests.get", playerId is null ? null : new PlayerWire(playerId), cancellationToken)
            .ConfigureAwait(false);
        return Convert(wire);
    }

    /// <summary>Tracks <paramref name="line"/> (null clears it) and returns the state read back
    /// from the game, so the caller can tell an applied change from a refused one.</summary>
    public async Task<LiveQuestState> TrackAsync(string? line, string? playerId = null, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<StateWire>("quests.track", new TrackWire(line ?? string.Empty, playerId), cancellationToken)
            .ConfigureAwait(false);
        return Convert(wire);
    }

    private static LiveQuestState Convert(StateWire wire) => new(
        IsNone(wire.TrackedLine) ? null : wire.TrackedLine,
        IsNone(wire.CurrentQuestLine) ? null : wire.CurrentQuestLine,
        wire.ActiveQuestsKnown ? (wire.ActiveQuests ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase) : null,
        wire.CanTrack, wire.IsHost, IsNone(wire.ServerQuest) ? null : wire.ServerQuest);

    private static bool IsNone(string? value) => string.IsNullOrEmpty(value) || value == "None";

    private sealed record PlayerWire(string PlayerId);
    private sealed record TrackWire(string Line, string? PlayerId);
    private sealed record StateWire(string? TrackedLine, string? CurrentQuestLine, bool ActiveQuestsKnown,
        IReadOnlyList<string>? ActiveQuests, bool CanTrack, bool IsHost, string? ServerQuest = null);
}

/// <param name="TrackedLine">The tracked quest line row, or null when none.</param>
/// <param name="WorldQuestLine">The quest line the whole world is on, or null when unreported.</param>
/// <param name="ActiveQuests">Objectives the game itself reports active, or null when the agent
/// could not ask (the app then judges state from the world flags).</param>
/// <param name="ServerQuest">The character's personal tutorial objective, or null when unreported.</param>
public sealed record LiveQuestState(string? TrackedLine, string? WorldQuestLine, IReadOnlySet<string>? ActiveQuests, bool CanTrack, bool IsHost, string? ServerQuest = null);
