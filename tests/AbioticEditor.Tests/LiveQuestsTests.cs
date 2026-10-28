using System.Text.Json;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>Community Update 5 multi-quest support in live editing (quests.get / quests.track).</summary>
public sealed class LiveQuestsTests
{
    private static readonly QuestCatalogData Catalog = new([], [new("QuestLine_Kylie", "Kylie", false, null, [])]);

    [Fact]
    public async Task Channel_reads_tracked_line_and_game_reported_active_objectives()
    {
        var channel = new FakeChannel();
        channel.Replies["quests.get"] = new { trackedLine = "QuestLine_Primary", currentQuestLine = "None", activeQuestsKnown = true,
            activeQuests = new[] { "quest_a", "quest_b" }, canTrack = true, isHost = true, serverQuest = "tutorial_find_bed" };

        var state = await new LiveQuestsChannel(channel).GetAsync();

        Assert.Equal("QuestLine_Primary", state.TrackedLine);
        Assert.Null(state.WorldQuestLine);
        Assert.Equal(2, state.ActiveQuests!.Count);
        Assert.Contains("QUEST_A", state.ActiveQuests);
        Assert.True(state.CanTrack);
        Assert.Equal("tutorial_find_bed", state.ServerQuest);
    }

    [Fact]
    public async Task Active_objectives_are_unknown_when_the_agent_could_not_ask_the_game()
    {
        var channel = new FakeChannel();
        channel.Replies["quests.get"] = new { trackedLine = "None", activeQuestsKnown = false, activeQuests = Array.Empty<string>(), canTrack = false, isHost = false };

        var state = await new LiveQuestsChannel(channel).GetAsync();

        Assert.Null(state.TrackedLine);
        Assert.Null(state.ActiveQuests);
        Assert.False(state.CanTrack);
    }

    [Fact]
    public async Task Tracking_sends_the_line_and_a_cleared_line_as_empty()
    {
        var channel = new FakeChannel();
        channel.Replies["quests.track"] = new { trackedLine = "QuestLine_Kylie", activeQuestsKnown = false, canTrack = true, isHost = true };
        var quests = new LiveQuestsChannel(channel);

        var state = await quests.TrackAsync("QuestLine_Kylie");
        Assert.Equal("QuestLine_Kylie", channel.LastPayload.GetProperty("line").GetString());
        Assert.Equal("QuestLine_Kylie", state.TrackedLine);

        await quests.TrackAsync(null);
        Assert.Equal(string.Empty, channel.LastPayload.GetProperty("line").GetString());
    }

    [Fact]
    public async Task Session_reports_quest_support_tracks_and_detects_a_refused_change()
    {
        var channel = new FakeChannel();
        channel.Replies["codex.get"] = new { emails = Array.Empty<string>(), journals = Array.Empty<string>(), fish = Array.Empty<string>(), compendium = Array.Empty<string>() };
        channel.Replies["quests.get"] = new { trackedLine = "QuestLine_Primary", activeQuestsKnown = true, activeQuests = new[] { "quest_a" }, canTrack = true, isHost = true };
        channel.Replies["flags.list"] = new { flags = new[] { new { name = "flag_a", isSet = true }, new { name = "flag_b", isSet = false } }, isHost = true };
        var session = await LivePlayerCodexSession.ConnectAsync(new LivePlayerCodexChannel(channel),
            quests: new LiveQuestsChannel(channel), flags: new LiveWorldFlagsChannel(channel));
        IPlayerQuestsSession quests = session;

        Assert.True(quests.SupportsMultipleQuests);
        Assert.True(quests.CanTrackQuestLines);
        Assert.True(quests.AppliesImmediately);
        Assert.Equal("QuestLine_Primary", quests.TrackedQuestLine);

        var view = await quests.LoadWorldViewAsync();
        Assert.Contains("quest_a", view!.ActiveQuests!);
        Assert.Contains("flag_a", view.Flags!);
        Assert.DoesNotContain("flag_b", view.Flags!);

        channel.Replies["quests.track"] = new { trackedLine = "QuestLine_Kylie", activeQuestsKnown = false, canTrack = true, isHost = true };
        await quests.TrackQuestLineAsync("QuestLine_Kylie", Catalog);
        Assert.Equal("QuestLine_Kylie", quests.TrackedQuestLine);

        channel.Replies["quests.track"] = new { trackedLine = "QuestLine_Kylie", activeQuestsKnown = false, canTrack = true, isHost = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => quests.TrackQuestLineAsync(null, Catalog));
    }

    [Fact]
    public async Task Tutorial_objective_is_the_first_active_quest_in_an_important_line_when_the_game_does_not_report_it()
    {
        // Community Update 5 has no readable ServerQuest; Abiotic_PlayerState.FindServerQuest picks the
        // first active quest (FindAllActiveQuests order) whose line is important. Checked against a
        // real migrated save: its stored ServerQuest was quest_evacuateoffices, the first such quest.
        var catalog = new QuestCatalogData([], [
            new("QuestLine_Kylie", "Kylie", false, null, ["quest_Kylie_FirstMeet"]),
            new("QuestLine_Primary", "", true, null, ["quest_evacuateoffices", "quest_entersilo3portal"])]);
        var channel = new FakeChannel();
        channel.Replies["codex.get"] = new { emails = Array.Empty<string>(), journals = Array.Empty<string>(), fish = Array.Empty<string>(), compendium = Array.Empty<string>() };
        channel.Replies["quests.get"] = new { trackedLine = "QuestLine_Primary", activeQuestsKnown = true,
            activeQuests = new[] { "quest_Kylie_FirstMeet", "quest_evacuateoffices", "quest_entersilo3portal" }, canTrack = true, isHost = true };
        IPlayerQuestsSession quests = await LivePlayerCodexSession.ConnectAsync(new LivePlayerCodexChannel(channel),
            quests: new LiveQuestsChannel(channel), flags: new LiveWorldFlagsChannel(channel));

        Assert.Equal("quest_evacuateoffices", quests.TutorialQuestFor(catalog));
        Assert.Equal("quest_evacuateoffices", QuestCatalog.ServerQuestFrom(["quest_Kylie_FirstMeet", "quest_evacuateoffices"], catalog));
        Assert.Null(QuestCatalog.ServerQuestFrom(["quest_Kylie_FirstMeet"], catalog));

        // An agent that does report it (older game builds) is trusted as is.
        channel.Replies["quests.get"] = new { trackedLine = "QuestLine_Primary", activeQuestsKnown = true,
            activeQuests = new[] { "quest_evacuateoffices" }, canTrack = true, isHost = true, serverQuest = "tutorial_bandage" };
        await quests.LoadWorldViewAsync();
        Assert.Equal("tutorial_bandage", quests.TutorialQuestFor(catalog));
    }

    [Fact]
    public async Task Session_hides_quests_when_the_agent_predates_them()
    {
        var channel = new FakeChannel();
        channel.Replies["codex.get"] = new { emails = Array.Empty<string>(), journals = Array.Empty<string>(), fish = Array.Empty<string>(), compendium = Array.Empty<string>() };
        var session = await LivePlayerCodexSession.ConnectAsync(new LivePlayerCodexChannel(channel),
            quests: new LiveQuestsChannel(channel), flags: new LiveWorldFlagsChannel(channel));

        Assert.False(((IPlayerQuestsSession)session).SupportsMultipleQuests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPlayerQuestsSession)session).TrackQuestLineAsync("QuestLine_Kylie", Catalog));
    }

    private sealed class FakeChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public Dictionary<string, object> Replies { get; } = [];
        public JsonElement LastPayload { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (payload is not null) LastPayload = JsonSerializer.SerializeToElement(payload, Options);
            if (!Replies.TryGetValue(command, out var reply)) throw new LiveAgentException($"unknown command {command}");
            return Task.FromResult(JsonSerializer.SerializeToElement(reply, Options).Deserialize<T>(Options)!);
        }
    }
}
