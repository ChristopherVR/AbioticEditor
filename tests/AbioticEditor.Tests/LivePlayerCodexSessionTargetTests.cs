using System.Text.Json;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public class LivePlayerCodexSessionTargetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_single_clear_or_unlock_keeps_its_original_player_and_ignores_stale_completion(bool known)
    {
        await using var channel = new DelayedCodexChannel();
        var session = await Connect(channel);
        var row = session.Emails.Single(item => item.Id == (known ? "a_pending" : "a_grant"));
        var original = row.IsKnown;
        channel.NextWrite = Completion();
        var notifications = 0;
        session.Changed += () => notifications++;

        var write = session.SetKnownAsync(row, known);
        Assert.False(write.IsCompleted);
        Assert.Equal("player-a", channel.LastWrite.GetProperty("playerId").GetString());
        if (known) Assert.Equal(row.Id, channel.LastWrite.GetProperty("emails")[0].GetString());
        else Assert.Equal("emails", channel.LastWrite.GetProperty("clear").GetProperty("section").GetString());

        await session.SwitchPlayerAsync("player-b");
        channel.NextWrite.SetResult(true);
        await write;

        Assert.Equal(original, row.IsKnown);
        Assert.Equal("player-b", session.SelectedPlayerId);
        Assert.True(session.HasCurrentHistory);
        Assert.True(session.Emails.Single(item => item.Id == "b_grant").IsKnown);
        Assert.False(session.Emails.Single(item => item.Id == "a_pending").IsKnown);
        Assert.False(session.Emails.Single(item => item.Id == "a_grant").IsKnown);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Delayed_bulk_unlock_does_not_mix_old_rows_into_new_email_or_fish_history()
    {
        await using var channel = new DelayedCodexChannel();
        var session = await Connect(channel);
        var email = session.Emails.Single(row => row.Id == "a_pending");
        var fish = session.Fish.Single(row => row.Id == "fish_pending");
        channel.NextWrite = Completion();
        var notifications = 0;
        session.Changed += () => notifications++;

        var write = session.SetKnownManyAsync([email, fish]);
        Assert.False(write.IsCompleted);
        Assert.Equal("player-a", channel.LastWrite.GetProperty("playerId").GetString());
        Assert.Equal("a_pending", channel.LastWrite.GetProperty("emails")[0].GetString());
        Assert.Equal("fish_pending", channel.LastWrite.GetProperty("fish")[0].GetString());
        await session.SwitchPlayerAsync("player-b");
        channel.NextWrite.SetResult(true);
        await write;

        Assert.False(email.IsKnown);
        Assert.False(fish.IsKnown);
        Assert.False(session.Emails.Single(row => row.Id == "a_pending").IsKnown);
        Assert.False(session.Fish.Single(row => row.Id == "fish_pending").IsKnown);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Old_read_cannot_replace_history_or_capabilities_after_switching_away_and_back()
    {
        await using var channel = new DelayedCodexChannel();
        var session = await Connect(channel);
        var read = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead = read;
        var notifications = 0;
        session.Changed += () => notifications++;
        var pending = session.RefreshAsync();
        Assert.False(pending.IsCompleted);

        await session.SwitchPlayerAsync("player-b");
        await session.SwitchPlayerAsync("player-a");
        read.SetResult("{\"emails\":[\"stale_grant\"],\"fish\":[\"stale_fish\"],\"canUnsetKnown\":false}");
        await pending;

        Assert.Equal("player-a", session.SelectedPlayerId);
        Assert.True(session.HasCurrentHistory);
        Assert.True(session.CanUnsetKnown);
        Assert.DoesNotContain(session.Emails, row => row.Id == "stale_grant");
        Assert.DoesNotContain(session.Fish, row => row.Id == "stale_fish");
        Assert.True(session.Emails.Single(row => row.Id == "a_grant").IsKnown);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task A_loading_target_has_no_current_history_and_rejects_writes_from_stale_rows()
    {
        await using var channel = new DelayedCodexChannel();
        var session = await Connect(channel);
        var oldRow = session.Emails.Single(row => row.Id == "a_pending");
        var read = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead = read;

        var change = session.SwitchPlayerAsync("player-b");
        Assert.Equal("player-b", session.SelectedPlayerId);
        Assert.False(session.HasCurrentHistory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetKnownAsync(oldRow, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetKnownManyAsync([oldRow]));
        Assert.Equal(JsonValueKind.Undefined, channel.LastWrite.ValueKind);

        read.SetResult("{\"emails\":[\"b_grant\"],\"canUnsetKnown\":true}");
        await change;
        Assert.True(session.HasCurrentHistory);
        Assert.True(session.Emails.Single(row => row.Id == "b_grant").IsKnown);
    }

    [Fact]
    public async Task A_switch_replaces_row_ownership_even_when_the_two_players_have_identical_history()
    {
        await using var channel = new DelayedCodexChannel { SameHistory = true };
        var session = await Connect(channel);
        var oldRow = session.Emails.Single(row => row.Id == "shared");

        await session.SwitchPlayerAsync("player-b");

        Assert.NotSame(oldRow, session.Emails.Single(row => row.Id == "shared"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetKnownAsync(oldRow, false));
        Assert.Equal(JsonValueKind.Undefined, channel.LastWrite.ValueKind);
    }

    [Fact]
    public async Task Same_player_refresh_during_an_unlock_updates_the_current_row_after_acknowledgement()
    {
        await using var channel = new DelayedCodexChannel();
        var session = await Connect(channel);
        var oldRow = session.Emails.Single(row => row.Id == "a_pending");
        channel.NextWrite = Completion();
        var write = session.SetKnownAsync(oldRow, true);
        channel.NextRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.NextRead.SetResult("{\"emails\":[\"a_grant\",\"another_grant\"],\"canUnsetKnown\":true}");
        await session.RefreshAsync();
        var current = session.Emails.Single(row => row.Id == "a_pending");
        Assert.NotSame(oldRow, current);
        Assert.False(current.IsKnown);

        channel.NextWrite.SetResult(true);
        await write;

        Assert.True(current.IsKnown);
        Assert.True(oldRow.IsKnown);
    }

    private static TaskCompletionSource<bool> Completion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<LivePlayerCodexSession> Connect(DelayedCodexChannel channel)
    {
        var session = await LivePlayerCodexSession.ConnectAsync(new LivePlayerCodexChannel(channel), "player-a");
        session.ApplyCodexVocabulary(new CodexVocabulary(
            [Email("a_grant"), Email("a_pending"), Email("b_grant"), Email("shared")], [], [],
            [new FishDefinition("fish_pending", "fish_item", false)]));
        return session;
    }

    private static EmailEntry Email(string id) => new(id, id, [], [], []);

    private sealed class DelayedCodexChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public bool SameHistory { get; init; }
        public TaskCompletionSource<bool>? NextWrite { get; set; }
        public TaskCompletionSource<string>? NextRead { get; set; }
        public JsonElement LastWrite { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "codex.get")
            {
                if (NextRead is { } pending)
                {
                    NextRead = null;
                    return JsonSerializer.Deserialize<TResponse>(await pending.Task, Options)!;
                }
                var player = JsonSerializer.SerializeToElement(payload, Options).GetProperty("playerId").GetString();
                var json = SameHistory ? "{\"emails\":[\"shared\"],\"canUnsetKnown\":true}"
                    : player == "player-b" ? "{\"emails\":[\"b_grant\",\"shared\"],\"canUnsetKnown\":true}"
                    : "{\"emails\":[\"a_grant\",\"shared\"],\"canUnsetKnown\":true}";
                return JsonSerializer.Deserialize<TResponse>(json, Options)!;
            }
            LastWrite = JsonSerializer.SerializeToElement(payload, Options);
            if (NextWrite is { } write) await write.Task;
            return default!;
        }
    }
}
