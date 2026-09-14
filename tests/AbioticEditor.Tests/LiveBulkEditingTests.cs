using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

// Exercise default interface dispatch exactly as the shared Razor tabs do.
#pragma warning disable CA1859
public sealed class LiveBulkEditingTests
{
    [Fact]
    public async Task Recipe_bulk_action_through_player_facade_sends_one_request_and_skips_known_rows()
    {
        var channel = new CaptureChannel();
        var recipes = await LivePlayerRecipesSession.ConnectAsync(new(channel), "player2");
        var ids = Enumerable.Range(0, 1000).Select(i => $"recipe{i}").ToArray();
        recipes.EnsureRecipeRows(ids);
        IPlayerRecipesSession facade = new LivePlayerEditorSession { RecipesSession = recipes };
        await facade.SetUnlockedManyAsync(ids.Concat(ids));
        Assert.Single(channel.Writes);
        Assert.Equal(1000, channel.Writes[0].GetProperty("unlockIds").GetArrayLength());
        Assert.Equal("player2", channel.Writes[0].GetProperty("playerId").GetString());
        Assert.Equal(1000, recipes.UnlockedRecipeCount);
        await facade.SetUnlockedManyAsync(ids);
        Assert.Single(channel.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Codex_bulk_action_groups_sections_and_only_marks_rows_after_success(bool fail)
    {
        var channel = new CaptureChannel { FailWrites = fail };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel), "player2");
        codex.ApplyCodexVocabulary(new(
            [new("email", "Email", [], [], [])], [new("note", "Note", "")],
            [new("lore", "Lore", null, null, [], ["Email", "Exploration"])], []));
        var rows = codex.Emails.Concat(codex.Journals).Concat(codex.Compendium).ToArray();
        IPlayerCodexSession facade = new LivePlayerEditorSession { CodexSession = codex };
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => facade.SetKnownManyAsync(rows));
        else await facade.SetKnownManyAsync(rows.Concat(rows));
        var write = Assert.Single(channel.Writes);
        Assert.Equal(1, write.GetProperty("emails").GetArrayLength());
        Assert.Equal(1, write.GetProperty("journals").GetArrayLength());
        Assert.Equal(2, write.GetProperty("compendium").GetArrayLength());
        Assert.Equal("player2", write.GetProperty("playerId").GetString());
        Assert.All(rows, row => Assert.Equal(!fail, row.IsKnown));
        if (!fail)
        {
            await facade.SetKnownManyAsync(rows);
            Assert.Single(channel.Writes);
        }
    }

    private sealed class CaptureChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public List<JsonElement> Writes { get; } = [];
        public bool FailWrites { get; init; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command.EndsWith(".get", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.Deserialize<TResponse>("{}", Options)!);
            Writes.Add(JsonSerializer.SerializeToElement(payload, Options));
            if (FailWrites) throw new InvalidOperationException("write failed");
            return Task.FromResult(default(TResponse)!);
        }
    }
}
