using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 112: the "WORLD-WIDE SEEN" browser <c>WorldStoryTab.razor</c> adds next to the world
/// recipes browser, backed by six new <see cref="IWorldStorySession"/> members
/// (<c>GlobalItemsPickedUpIds</c>, <c>GlobalEmailsReadIds</c>, <c>GlobalJournalEntryIds</c>,
/// <c>GlobalCompendiumEmailIds</c>/<c>GlobalCompendiumNarrativeIds</c>/<c>GlobalCompendiumExplorationIds</c>,
/// <c>CanEditGlobalLists</c>, <c>SetGlobalListAsync</c>) that move round 106/107's live-only
/// <c>LiveStorySession</c> plumbing (see <see cref="LiveCodexKillSectionsAndWorldGlobalListsTests"/>)
/// onto the shared interface the file session (<see cref="WorldSaveSession"/>) now also implements,
/// staged/written the same way the world recipes browser already is (see
/// <see cref="GlobalUnlockWriterTests"/> for the underlying writer coverage).
/// </summary>
public sealed class WorldStoryGlobalListsBrowserTests
{
    private static string? MetadataSave => Fixtures.ServerWorldsDir is { } dir
        ? Path.Combine(dir, "WorldSave_MetaData.sav")
        : null;

    // ---------- offline: WorldSaveSession, staged until Save, written by WorldSaveWriter ----------

    [Fact]
    public async Task WorldSaveSession_adds_a_row_through_the_shared_interface_and_it_survives_a_round_trip()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        var scratch = Directory.CreateTempSubdirectory("abiotic-worldwideseen-");
        try
        {
            var copy = Path.Combine(scratch.FullName, "WorldSave_MetaData.sav");
            File.Copy(path, copy);
            var originalBytes = File.ReadAllBytes(copy);

            var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            Assert.True(session.SupportsGlobalLists);
            Assert.True(session.CanEditGlobalLists);
            const string sentinel = "WorldStoryGlobalLists_TestItem";
            Assert.DoesNotContain(sentinel, session.GlobalItemsPickedUpIds);

            await session.SetGlobalListAsync("itemsPickedUp", [sentinel], true);
            Assert.Contains(sentinel, session.GlobalItemsPickedUpIds);
            Assert.True(session.IsDirty);
            // Staged only: nothing on disk yet.
            Assert.Equal(originalBytes, File.ReadAllBytes(copy));

            await session.SaveAsync();
            Assert.False(session.IsDirty);
            Assert.True(File.Exists(copy + ".bak"));
            // The .bak is the pre-edit save byte-for-byte: WorldSaveWriter's mutate-in-place
            // contract (see CLAUDE.md / GlobalUnlockWriterTests) means the rewritten file itself
            // differs only in the touched array, and the backup proves what "before" looked like.
            Assert.Equal(originalBytes, File.ReadAllBytes(copy + ".bak"));

            var reread = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            Assert.Contains(sentinel, reread.GlobalItemsPickedUpIds);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WorldSaveSession_removes_a_row_through_the_shared_interface_and_the_removal_survives_a_round_trip()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        var scratch = Directory.CreateTempSubdirectory("abiotic-worldwideseen-remove-");
        try
        {
            var copy = Path.Combine(scratch.FullName, "WorldSave_MetaData.sav");
            File.Copy(path, copy);
            const string sentinel = "WorldStoryGlobalLists_RemoveMe";

            // Seed the row first (add, save), then remove it through the same interface.
            var seed = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            await seed.SetGlobalListAsync("emailsRead", [sentinel], true);
            await seed.SaveAsync();
            var withRow = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            Assert.Contains(sentinel, withRow.GlobalEmailsReadIds);

            await withRow.SetGlobalListAsync("emailsRead", [sentinel], false);
            Assert.DoesNotContain(sentinel, withRow.GlobalEmailsReadIds);
            await withRow.SaveAsync();

            var final = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            Assert.DoesNotContain(sentinel, final.GlobalEmailsReadIds);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WorldSaveSession_refuses_an_unknown_list_name()
    {
        if (MetadataSave is not { } path || !File.Exists(path)) return;

        var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(path), path);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SetGlobalListAsync("somethingElse", ["x"], true));
    }

    // ---------- live: LiveStorySession routes to the round-107 LiveWorldUnlocksChannel ----------

    [Fact]
    public async Task LiveStorySession_SupportsGlobalLists_is_false_when_the_agent_predates_the_six_lists()
    {
        // Simulates an older agent build: worldunlocks.get itself fails (the same fallback path
        // ConnectAsync already takes for a build that predates the command entirely).
        var channel = new Channel { ThrowOnWorldUnlocks = true, Read = new { isHost = true } };
        var session = await LiveStorySession.ConnectAsync(new(channel), new(channel), new(channel), new(channel));
        var story = session;

        Assert.False(story.SupportsGlobalLists);
        Assert.False(story.CanEditGlobalLists);
    }

    [Fact]
    public async Task LiveStorySession_SupportsGlobalLists_is_true_once_the_agent_reports_the_six_lists()
    {
        var channel = new Channel
        {
            Read = new { isHost = true, canEditGlobalLists = true, itemsPickedUp = new[] { "scrap_metal" }, emailsRead = new[] { "Email_Crossbow" } },
        };
        var session = await LiveStorySession.ConnectAsync(new(channel), new(channel), new(channel), new(channel));
        var story = session;

        Assert.True(story.SupportsGlobalLists);
        Assert.True(story.CanEditGlobalLists);
        Assert.Contains("scrap_metal", story.GlobalItemsPickedUpIds);
        Assert.Contains("Email_Crossbow", story.GlobalEmailsReadIds);
        Assert.Empty(story.GlobalJournalEntryIds);
        Assert.Empty(story.GlobalCompendiumEmailIds);
        Assert.Empty(story.GlobalCompendiumNarrativeIds);
        Assert.Empty(story.GlobalCompendiumExplorationIds);

        await story.SetGlobalListAsync("journalEntries", ["Journal_Radio"], true);
        var edit = Assert.Single(channel.Write.GetProperty("journalEntries").EnumerateArray());
        Assert.Equal("Journal_Radio", edit.GetProperty("id").GetString());
        Assert.True(edit.GetProperty("present").GetBoolean());
    }

    // ---------- shared UI: one component, both session kinds ----------

    [Fact]
    public void WorldStoryTab_renders_the_world_wide_seen_section_gated_on_the_shared_interface()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldStoryTab.razor");
        Assert.Contains("Session.SupportsGlobalLists", source, StringComparison.Ordinal);
        Assert.Contains("Session.CanEditGlobalLists", source, StringComparison.Ordinal);
        Assert.Contains("Session.SetGlobalListAsync", source, StringComparison.Ordinal);
        Assert.Contains("WorldStory_WorldWideSeen", source, StringComparison.Ordinal);
        // Bound to the shared IWorldStorySession parameter (not a live-only or file-only branch),
        // the same "one component, both hosts" rule LiveWorldUiParityContractTests checks for the
        // rest of this tab.
        Assert.Contains("IWorldStorySession Session", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_session_kinds_implement_the_world_wide_list_members()
    {
        var fileSession = UiSource.ReadAllText("Models", "WorldSaveSession.cs");
        Assert.Contains("SupportsGlobalLists", fileSession, StringComparison.Ordinal);
        Assert.Contains("CanEditGlobalLists", fileSession, StringComparison.Ordinal);
        Assert.Contains("SetGlobalListAsync", fileSession, StringComparison.Ordinal);

        var liveSession = UiSource.ReadAllText("Models", "LiveStorySession.cs");
        Assert.Contains("SupportsGlobalLists", liveSession, StringComparison.Ordinal);
        Assert.Contains("CanEditGlobalLists", liveSession, StringComparison.Ordinal);
        Assert.Contains("SetGlobalListAsync", liveSession, StringComparison.Ordinal);
    }

    private sealed class Channel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public object Read { get; set; } = new { };
        public bool ThrowOnWorldUnlocks { get; set; }
        public JsonElement Write { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "worldunlocks.get" && ThrowOnWorldUnlocks)
                throw new LiveAgentException("agent build predates worldunlocks.get");
            if (command.EndsWith(".get", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.SerializeToElement(Read, Options).Deserialize<T>(Options)!);
            Write = JsonSerializer.SerializeToElement(payload, Options);
            return Task.FromResult(default(T)!);
        }
    }
}
