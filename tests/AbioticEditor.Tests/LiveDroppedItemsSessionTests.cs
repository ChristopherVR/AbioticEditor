using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// The live GROUND ITEMS list must not show an item the player just deleted, even when the game
/// (still running a Lua bundle from before round 91) keeps listing the destroyed actor until its
/// garbage collector runs. The session hides confirmed-removed ids until the list itself stops
/// reporting them, and never hides anything the game did not confirm.
/// </summary>
public class LiveDroppedItemsSessionTests
{
    private static readonly object Listing = new
    {
        items = new[]
        {
            new { id = "Abiotic_Item_Dropped_C_1", itemId = "scrap_metal", stack = 1, x = 0.0, y = 0.0, z = 0.0 },
            new { id = "Abiotic_Item_Dropped_C_2", itemId = "scrap_cloth", stack = 2, x = 0.0, y = 0.0, z = 0.0 },
        },
        isHost = true,
    };

    [Fact]
    public async Task A_confirmed_remove_hides_the_row_even_when_the_game_still_lists_it()
    {
        var channel = new ScriptedChannel { List = Listing, Remove = new { removed = 1, stuck = 0 } };
        var session = await LiveDroppedItemsSession.ConnectAsync(new LiveDroppedItemsChannel(channel));
        Assert.Equal(2, session.DroppedItems.Count);

        await session.RemoveDroppedItemAsync("Abiotic_Item_Dropped_C_1");

        Assert.Equal("Removed from the running game.", session.Status);
        Assert.Single(session.DroppedItems);
        Assert.Equal("Abiotic_Item_Dropped_C_2", session.DroppedItems[0].Id);

        // The periodic refresh keeps hiding it while the game keeps listing it ...
        await session.RefreshAsync();
        Assert.Single(session.DroppedItems);

        // ... and forgets it as soon as the game stops, so a later actor reusing the name shows.
        channel.List = new { items = new[] { new { id = "Abiotic_Item_Dropped_C_2", itemId = "scrap_cloth", stack = 2, x = 0.0, y = 0.0, z = 0.0 } }, isHost = true };
        await session.RefreshAsync();
        channel.List = Listing;
        await session.RefreshAsync();
        Assert.Equal(2, session.DroppedItems.Count);
    }

    [Fact]
    public async Task An_unconfirmed_remove_hides_nothing_and_says_so()
    {
        var channel = new ScriptedChannel { List = Listing, Remove = new { removed = 0, stuck = 1 } };
        var session = await LiveDroppedItemsSession.ConnectAsync(new LiveDroppedItemsChannel(channel));

        await session.RemoveDroppedItemAsync("Abiotic_Item_Dropped_C_1");

        Assert.Equal(2, session.DroppedItems.Count);
        Assert.Contains("still lying there", session.Status);
    }

    [Fact]
    public async Task A_partial_batch_hides_nothing_but_reports_the_split()
    {
        var channel = new ScriptedChannel { List = Listing, Remove = new { removed = 1 } };
        var session = await LiveDroppedItemsSession.ConnectAsync(new LiveDroppedItemsChannel(channel));

        await session.RemoveDroppedItemsAsync(["Abiotic_Item_Dropped_C_1", "Abiotic_Item_Dropped_C_2"]);

        Assert.Equal(2, session.DroppedItems.Count);
        Assert.Equal("Removed 1 of 2 from the running game.", session.Status);
    }

    /// <summary>
    /// Regression test for the "Delete doesn't remove the item immediately - it stays for a good
    /// few seconds" live-mode bug: RemoveDroppedItemsAsync must drop the confirmed-removed row
    /// from <see cref="LiveDroppedItemsSession.DroppedItems"/> the instant the awaited
    /// <c>dropped.remove</c> reply comes back, not after the "dropped.list" world scan it fires
    /// afterwards as reconciliation. The gate below holds that follow-up scan open for the whole
    /// assertion, so a regression that went back to awaiting it inline would hang instead of just
    /// happening to pass quickly.
    /// </summary>
    [Fact]
    public async Task Remove_hides_the_row_before_the_reconciling_list_scan_completes()
    {
        var channel = new ScriptedChannel { List = Listing, Remove = new { removed = 1, stuck = 0 } };
        var session = await LiveDroppedItemsSession.ConnectAsync(new LiveDroppedItemsChannel(channel));
        Assert.Equal(2, session.DroppedItems.Count);

        channel.ArmListGate();
        await session.RemoveDroppedItemAsync("Abiotic_Item_Dropped_C_1");

        Assert.Single(session.DroppedItems);
        Assert.Equal("Abiotic_Item_Dropped_C_2", session.DroppedItems[0].Id);

        channel.ReleaseListGate();
    }

    private sealed class ScriptedChannel : ILiveGameChannel
    {
        private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);
        public object? List { get; set; }
        public object? Remove { get; set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // Lets a test hold "dropped.list" open to prove a caller does not (and, after this session's
        // background-reconciliation fix, no longer needs to) wait on it.
        private TaskCompletionSource? _listGate;
        public void ArmListGate() => _listGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseListGate() => _listGate?.TrySetResult();

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "dropped.list" && _listGate is { } gate) await gate.Task.ConfigureAwait(false);
            var response = command switch
            {
                "dropped.list" => List,
                "dropped.remove" => Remove,
                _ => throw new InvalidOperationException(command),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(response, Json);
            return System.Text.Json.JsonSerializer.Deserialize<TResponse>(json, Json)!;
        }
    }
}
