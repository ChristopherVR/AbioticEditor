using System.Text.Json;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

[CollectionDefinition("Live item table index", DisableParallelization = true)]
public sealed class LiveItemTableIndexFixture;

[Collection("Live item table index")]
public sealed class LiveItemTableWireTests
{
    [Fact]
    public async Task All_live_item_writers_send_the_catalog_table_for_supplemental_items()
    {
        const string path = "/Game/Blueprints/Items/ItemTable_Gear.ItemTable_Gear";
        ItemTableIndex.Set(new Dictionary<string, string> { ["backpack_test"] = path });
        try
        {
            var channel = new CaptureChannel();
            await new LiveInventoryChannel(channel).SetAsync([new("equip", 6, ItemId: "backpack_test", Stack: 1)]);
            Assert.Equal(path, channel.Payload.GetProperty("edits")[0].GetProperty("dataTable").GetString());
            Assert.Equal("equip", channel.Payload.GetProperty("edits")[0].GetProperty("kind").GetString());
            await new LiveContainersChannel(channel).SetAsync("crate", [new(0, ItemId: "backpack_test", Stack: 1)]);
            Assert.Equal(path, channel.Payload.GetProperty("edits")[0].GetProperty("dataTable").GetString());
            await new LiveDroppedItemsChannel(channel).AddAsync("backpack_test", 1);
            Assert.Equal(path, channel.Payload.GetProperty("dataTable").GetString());
            await new LiveCompanionsChannel(channel).SetAsync("hotbar", 0,
                new CarriedPet(PetSlotKind.Hotbar, 0, "backpack_test", null, 10, 10, 0, 0, 0));
            Assert.Equal(path, channel.Payload.GetProperty("dataTable").GetString());
        }
        finally { ItemTableIndex.Set(new Dictionary<string, string>()); }
    }

    [Fact]
    public async Task Clearing_or_editing_only_quantity_does_not_invent_a_table()
    {
        var channel = new CaptureChannel();
        await new LiveInventoryChannel(channel).SetAsync([new("backpack", 0, Clear: true), new("backpack", 1, Stack: 2)]);
        foreach (var row in channel.Payload.GetProperty("edits").EnumerateArray())
            Assert.Equal(JsonValueKind.Null, row.GetProperty("dataTable").ValueKind);
    }

    private sealed class CaptureChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public JsonElement Payload { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions);
            return Task.FromResult(default(TResponse)!);
        }
    }
}
