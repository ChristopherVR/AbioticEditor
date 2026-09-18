using System.Text.Json;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 106: covers the two partial live-editing gaps closed this round that have a C#-side
/// surface (the third, recipe relock, was already fully implemented and only needed Lua harness
/// coverage - see <c>tests/cases/recipes.lua</c> and docs/PROGRESS.md's round-107 entry).
///
/// Gap B - kill-tracked COMPENDIUM sections: <see cref="LivePlayerCodexSession"/> now widens a
/// row's editability/section types with a live-only "KillRequirement" entry when the connected
/// agent reports <c>canUnlockKillSections</c>, without touching the shared <see cref="CodexCatalog"/>
/// model the offline file session also uses (see <c>LivePlayerCodexChannel</c>'s remarks).
///
/// Gap C - world-wide item/codex lists: <see cref="LiveStorySession"/> can now add/remove rows in
/// the six <c>Global*</c> <c>FArrayProperty</c> lists on <c>Abiotic_Survival_GameState_C</c>,
/// gated by a capability independent of the recipe TSets' extra runtime check (see
/// <see cref="AbioticEditor.Core.LiveEditing.World.LiveWorldUnlocksChannel"/>'s remarks).
/// </summary>
public sealed class LiveCodexKillSectionsAndWorldGlobalListsTests
{
    [Fact]
    public async Task Kill_tracked_compendium_row_becomes_editable_only_when_the_agent_reports_support()
    {
        var channel = new Channel { Read = new { compendium = Array.Empty<string>(), canUnlockKillSections = true } };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel));
        var vocabulary = new CodexVocabulary([], [],
            [new CompendiumEntry("Compendium_Kill", "Kill Entry", null, null, [], [], KillRequired: 3)], []);
        codex.ApplyCodexVocabulary(vocabulary);

        var row = Assert.Single(codex.Compendium);
        Assert.True(row.Editable);
        Assert.Equal("KillRequirement", Assert.Single(row.SectionTypes));

        await codex.SetKnownAsync(row, true);
        var pair = Assert.Single(channel.Write.GetProperty("compendium").EnumerateArray());
        Assert.Equal("Compendium_Kill", pair.GetProperty("row").GetString());
        Assert.Equal("KillRequirement", pair.GetProperty("sectionType").GetString());
        Assert.True(row.IsKnown);
    }

    [Fact]
    public async Task Kill_tracked_compendium_row_stays_readonly_against_an_older_agent()
    {
        // canUnlockKillSections omitted from the wire reply, exactly like an older agent build.
        var channel = new Channel { Read = new { compendium = Array.Empty<string>() } };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel));
        var vocabulary = new CodexVocabulary([], [],
            [new CompendiumEntry("Compendium_Kill", "Kill Entry", null, null, [], [], KillRequired: 3)], []);
        codex.ApplyCodexVocabulary(vocabulary);

        var row = Assert.Single(codex.Compendium);
        Assert.False(row.Editable);
        Assert.Empty(row.SectionTypes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => codex.SetKnownAsync(row, true));
    }

    [Fact]
    public async Task A_row_with_a_real_section_type_is_unaffected_by_kill_section_support()
    {
        // KillRequired is null here, so canUnlockKillSections must not add anything extra.
        var channel = new Channel { Read = new { compendium = Array.Empty<string>(), canUnlockKillSections = true } };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel));
        var vocabulary = new CodexVocabulary([], [],
            [new CompendiumEntry("Compendium_Radio", "Radio", null, null, [], ["Exploration"])], []);
        codex.ApplyCodexVocabulary(vocabulary);

        var row = Assert.Single(codex.Compendium);
        Assert.True(row.Editable);
        Assert.Equal("Exploration", Assert.Single(row.SectionTypes));
    }

    [Fact]
    public async Task World_global_list_edits_use_capability_gating_independent_of_recipe_TSet_support()
    {
        // canEditRecipes is false (older/unsupported runtime for the TSets), but the six plain
        // FArrayProperty lists need no TSet support, so canEditGlobalLists is still true.
        var channel = new Channel
        {
            Read = new { isHost = true, canEditRecipes = false, canEditGlobalLists = true, itemsPickedUp = new[] { "old" } },
        };
        var session = await LiveStorySession.ConnectAsync(new(channel), new(channel), new(channel), new(channel));
        Assert.False(session.CanEditGlobalRecipes);
        Assert.True(session.CanEditGlobalLists);
        Assert.Null(session.GlobalListEditsUnavailableReason);
        Assert.Equal("old", Assert.Single(session.GlobalItemsPickedUpIds));

        await session.SetGlobalListAsync("itemsPickedUp", ["new", "new", ""], true);
        var edit = Assert.Single(channel.Write.GetProperty("itemsPickedUp").EnumerateArray());
        Assert.Equal("new", edit.GetProperty("id").GetString());
        Assert.True(edit.GetProperty("present").GetBoolean());
    }

    [Fact]
    public async Task World_global_list_edits_are_refused_without_the_capability()
    {
        var channel = new Channel
        {
            Read = new { isHost = false, canEditGlobalLists = false, globalListEditsUnavailableReason = "not-host" },
        };
        var session = await LiveStorySession.ConnectAsync(new(channel), new(channel), new(channel), new(channel));
        Assert.False(session.CanEditGlobalLists);
        Assert.Equal("not-host", session.GlobalListEditsUnavailableReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetGlobalListAsync("itemsPickedUp", ["new"], true));
        Assert.Equal(JsonValueKind.Undefined, channel.Write.ValueKind);
    }

    private sealed class Channel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public object Read { get; set; } = new { };
        public JsonElement Write { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command.EndsWith(".get", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.SerializeToElement(Read, Options).Deserialize<T>(Options)!);
            Write = JsonSerializer.SerializeToElement(payload, Options);
            return Task.FromResult(default(T)!);
        }
    }
}
