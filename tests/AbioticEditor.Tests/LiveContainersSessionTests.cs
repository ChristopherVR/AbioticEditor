using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Exercises <see cref="LiveContainersSession"/>'s refresh surface against a fake
/// <see cref="ILiveGameChannel"/> that answers "containers.list"/"containers.set" from an
/// in-memory table, mirroring <see cref="LiveInventorySessionTests"/>'s pattern. Covers the
/// "keep the open tab honest" requirement: a periodic <c>RefreshAsync()</c> call must pick up a
/// change made out from under the session (another player, a world trigger) and announce it
/// through <see cref="LiveContainersSession.Changed"/> so a visible tab knows to redraw.
/// </summary>
public sealed class LiveContainersSessionTests
{
    [Fact]
    public async Task RefreshAsync_picks_up_a_slot_changed_out_from_under_the_session_and_raises_Changed()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", x: 1, y: 2, z: 3);
        channel.SetSlot("c1", 0, "Item_Bandage", stack: 2);

        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));

        var container = Assert.Single(session.Containers);
        Assert.Equal("Item_Bandage", container.Inventories[0].Slots[0].ItemId);

        // Something else in the running game (another player, a world trigger) changes the
        // container's contents without this session's involvement.
        channel.SetSlot("c1", 0, "Item_Rope", stack: 5);

        var raised = 0;
        session.Changed += () => raised++;

        await session.RefreshAsync();

        var refreshed = Assert.Single(session.Containers);
        Assert.Equal("c1", refreshed.Id);
        Assert.Equal("Item_Rope", refreshed.Inventories[0].Slots[0].ItemId);
        Assert.Equal(5, refreshed.Inventories[0].Slots[0].Count);
        Assert.Equal(1, raised);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Setting_a_slot_applies_immediately_and_raises_Changed()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", x: 0, y: 0, z: 0);
        channel.SetSlot("c1", 0, isEmpty: true);

        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));

        var raised = 0;
        session.Changed += () => raised++;

        var slot = new InventoryItemSlot(0, "Item_Torch", 1, Durability: 0, MaxDurability: 0,
            AmmoInMagazine: 0, LiquidLevel: 0, LiquidType: null, DynamicState: false, PlayerMadeString: null, AssetId: null);
        var applied = await session.TrySetContainerSlotAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, slot);

        Assert.True(applied);
        Assert.Equal(1, raised);
        Assert.Equal("Item_Torch", session.Containers[0].Inventories[0].Slots[0].ItemId);
        Assert.Null(session.Status);
    }

    private sealed class FakeContainersChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, (string Label, double X, double Y, double Z)> _containers = new(StringComparer.Ordinal);
        private readonly Dictionary<(string ContainerId, int SlotIndex), SlotState> _slots = new();

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetContainer(string id, string label, double x, double y, double z) => _containers[id] = (label, x, y, z);

        public void SetSlot(string containerId, int slotIndex, string? itemId = null, int stack = 0, bool isEmpty = false)
            => _slots[(containerId, slotIndex)] = new SlotState(itemId ?? "Empty", isEmpty || itemId is null, stack, 0, 0);

        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "containers.list" => new
                {
                    containers = _containers.Select(kv => new
                    {
                        id = kv.Key,
                        label = kv.Value.Label,
                        x = kv.Value.X,
                        y = kv.Value.Y,
                        z = kv.Value.Z,
                        slots = _slots.Where(s => s.Key.ContainerId == kv.Key).Select(s => new
                        {
                            slotIndex = s.Key.SlotIndex,
                            itemId = s.Value.ItemId,
                            isEmpty = s.Value.IsEmpty,
                            stack = s.Value.Stack,
                            durability = s.Value.Durability,
                            maxDurability = s.Value.MaxDurability,
                        }).ToList(),
                    }).ToList(),
                    isHost = true,
                },
                "containers.set" => ApplySet(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(element.Deserialize<TResponse>(JsonOptions)!);
        }

        private object? ApplySet(JsonElement payload)
        {
            var containerId = payload.GetProperty("id").GetString()!;
            if (!payload.TryGetProperty("edits", out var edits)) return null;
            foreach (var edit in edits.EnumerateArray())
            {
                var slotIndex = edit.GetProperty("slotIndex").GetInt32();
                var clear = edit.TryGetProperty("clear", out var clearProp) && clearProp.ValueKind == JsonValueKind.True;
                if (clear)
                {
                    _slots[(containerId, slotIndex)] = new SlotState("Empty", true, 0, 0, 0);
                    continue;
                }
                var itemId = edit.TryGetProperty("itemId", out var itemProp) && itemProp.ValueKind == JsonValueKind.String
                    ? itemProp.GetString()
                    : null;
                var stack = edit.TryGetProperty("stack", out var stackProp) && stackProp.ValueKind == JsonValueKind.Number
                    ? stackProp.GetInt32() : 0;
                _slots[(containerId, slotIndex)] = new SlotState(itemId ?? "Empty", itemId is null, stack, 0, 0);
            }
            return null;
        }

        private sealed record SlotState(string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability);
    }
}
