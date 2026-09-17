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
        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var response = command switch
            {
                "dropped.list" => List,
                "dropped.remove" => Remove,
                _ => throw new InvalidOperationException(command),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(response, Json);
            return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<TResponse>(json, Json)!);
        }
    }
}
