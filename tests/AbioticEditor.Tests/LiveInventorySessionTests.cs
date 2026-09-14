using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Exercises <see cref="LiveInventorySession"/> against a fake <see cref="ILiveGameChannel"/>
/// that behaves like the real live-agent wire protocol (JSON request/response, one in-flight
/// request at a time - see TcpLiveGameChannel/FileMailbox) but answers "inventory.list" from an
/// in-memory slot table instead of a running game. This is the regression test for the bug
/// report "moving items into the inventory of a player doesn't seem to show" (round 2026-09):
/// after a live hotbar-to-backpack move, the destination slot object must reflect the moved item
/// immediately, on the SAME <see cref="PlayerInventorySlotEdit"/> instance the UI is bound to
/// (see LiveInventorySession.RefreshAsync's in-place update contract).
/// </summary>
public sealed class LiveInventorySessionTests
{
    [Fact]
    public async Task Ammo_is_read_edited_and_preserved_when_moving_a_weapon()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("hotbar", 0, "weapon_test", stack: 1, ammo: 12);
        channel.SetSlot("backpack", 0, isEmpty: true);
        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));
        var weapon = Assert.Single(session.Hotbar);
        Assert.Equal(12, weapon.AmmoInMagazine);
        weapon.AmmoInMagazine = 7;
        await session.PushSlotAsync(PlayerInventoryArea.Hotbar, weapon);
        Assert.Equal(7, weapon.AmmoInMagazine);
        Assert.True(await session.TrySwapInventorySlotsAsync(PlayerInventoryArea.Hotbar, 0, PlayerInventoryArea.Backpack, 0));
        Assert.Equal(7, Assert.Single(session.Backpack).AmmoInMagazine);
        Assert.Equal(0, weapon.AmmoInMagazine);
    }

    [Fact]
    public async Task Swap_moves_a_hotbar_item_into_an_empty_backpack_slot_and_updates_both_lists_in_place()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("hotbar", 0, "Item_Bandage", stack: 3);
        channel.SetSlot("backpack", 0, isEmpty: true);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));

        var hotbarSlot = session.Hotbar.Single(s => s.Index == 0);
        var backpackSlot = session.Backpack.Single(s => s.Index == 0);
        Assert.False(hotbarSlot.IsEmpty);
        Assert.True(backpackSlot.IsEmpty);

        var moved = await session.TrySwapInventorySlotsAsync(
            PlayerInventoryArea.Hotbar, 0, PlayerInventoryArea.Backpack, 0);

        Assert.True(moved);

        // Same objects, mutated in place - a UI bound to these specific instances (rather than
        // re-fetching Session.Backpack/Session.Hotbar after the await) must see the update.
        Assert.True(ReferenceEquals(hotbarSlot, session.Hotbar.Single(s => s.Index == 0)));
        Assert.True(ReferenceEquals(backpackSlot, session.Backpack.Single(s => s.Index == 0)));

        Assert.True(hotbarSlot.IsEmpty);
        Assert.False(backpackSlot.IsEmpty);
        Assert.Equal("Item_Bandage", backpackSlot.ItemId);
        Assert.Equal(3, backpackSlot.Count);
    }

    [Fact]
    public async Task PushSlotAsync_sets_an_item_into_an_empty_slot_and_updates_the_same_instance()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("backpack", 4, isEmpty: true);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));
        var slot = session.Backpack.Single(s => s.Index == 4);
        Assert.True(slot.IsEmpty);

        slot.ItemId = "Item_Bandage";
        slot.Count = 5;
        await session.PushSlotAsync(PlayerInventoryArea.Backpack, slot);

        // The wire actually carried the edit (not just the local mirror).
        var wireSlot = channel.LastSetEdits.Single(e => e.Kind == "backpack" && e.SlotIndex == 4);
        Assert.Equal("Item_Bandage", wireSlot.ItemId);
        Assert.Equal(5, wireSlot.Stack);
        Assert.False(wireSlot.Clear);

        // Same object the UI is bound to, refreshed from the game's own answer.
        Assert.True(ReferenceEquals(slot, session.Backpack.Single(s => s.Index == 4)));
        Assert.False(slot.IsEmpty);
        Assert.Equal("Item_Bandage", slot.ItemId);
        Assert.Equal(5, slot.Count);
    }

    [Fact]
    public async Task PushSlotAsync_quick_gives_into_the_first_empty_hotbar_slot()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("hotbar", 0, isEmpty: true);
        channel.SetSlot("hotbar", 1, isEmpty: true);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));

        // Mirrors PlayerInventoryTab.QuickGiveFallback: first empty hotbar slot.
        var target = session.Hotbar.First(s => s.IsEmpty);
        target.ItemId = "Item_Flashlight";
        target.Count = 1;
        await session.PushSlotAsync(PlayerInventoryArea.Hotbar, target);

        var wireSlot = channel.LastSetEdits.Single(e => e.Kind == "hotbar" && e.SlotIndex == target.Index);
        Assert.Equal("Item_Flashlight", wireSlot.ItemId);
        Assert.Equal(1, wireSlot.Stack);

        Assert.False(session.Hotbar.Single(s => s.Index == target.Index).IsEmpty);
    }

    [Fact]
    public async Task PushSlotAsync_clears_a_slot_and_sends_a_clear_edit()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("backpack", 2, "Item_Rope", stack: 4);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));
        var slot = session.Backpack.Single(s => s.Index == 2);
        Assert.False(slot.IsEmpty);

        slot.Clear();
        await session.PushSlotAsync(PlayerInventoryArea.Backpack, slot);

        var wireSlot = channel.LastSetEdits.Single(e => e.Kind == "backpack" && e.SlotIndex == 2);
        Assert.True(wireSlot.Clear);

        Assert.True(ReferenceEquals(slot, session.Backpack.Single(s => s.Index == 2)));
        Assert.True(slot.IsEmpty);
        Assert.Equal(0, slot.Count);
    }

    [Fact]
    public async Task RefreshAsync_updates_the_same_slot_instances_in_place_and_raises_Changed()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("backpack", 0, "Item_Rope", stack: 1);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));
        var slot = session.Backpack.Single(s => s.Index == 0);

        var changedCount = 0;
        session.Changed += () => changedCount++;

        // Something else changed the game state (another live edit, another player, the game
        // itself) without going through this session's own mutation methods - exactly what the
        // host's periodic background poll (RefreshAsync on a timer) exists to pick up.
        channel.SetSlot("backpack", 0, "Item_Bandage", stack: 9);
        await session.RefreshAsync();

        Assert.True(ReferenceEquals(slot, session.Backpack.Single(s => s.Index == 0)));
        Assert.Equal("Item_Bandage", slot.ItemId);
        Assert.Equal(9, slot.Count);
        Assert.True(changedCount > 0);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Swap_exchanges_items_between_two_occupied_slots_in_different_areas()
    {
        var channel = new FakeInventoryChannel();
        channel.SetSlot("hotbar", 2, "Item_Torch", stack: 1);
        channel.SetSlot("backpack", 5, "Item_Rope", stack: 4);

        var session = await LiveInventorySession.ConnectAsync(new LiveInventoryChannel(channel));

        var moved = await session.TrySwapInventorySlotsAsync(
            PlayerInventoryArea.Hotbar, 2, PlayerInventoryArea.Backpack, 5);

        Assert.True(moved);
        var hotbarSlot = session.Hotbar.Single(s => s.Index == 2);
        var backpackSlot = session.Backpack.Single(s => s.Index == 5);
        Assert.Equal("Item_Rope", hotbarSlot.ItemId);
        Assert.Equal(4, hotbarSlot.Count);
        Assert.Equal("Item_Torch", backpackSlot.ItemId);
        Assert.Equal(1, backpackSlot.Count);
    }

    /// <summary>
    /// A fake <see cref="ILiveGameChannel"/> that round-trips payloads/results through JSON the
    /// same way <c>TcpLiveGameChannel</c> does, so a bug in a channel's own wire-shape records
    /// would show up here too, not just a bug in the session's own state juggling.
    /// </summary>
    private sealed class FakeInventoryChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<(string Kind, int Index), SlotState> _slots = new();

        /// <summary>The edits carried by the most recent "inventory.set" call, so a test can
        /// assert the wire actually got the mutation - not just that the local mirror changed.</summary>
        public IReadOnlyList<SentEdit> LastSetEdits { get; private set; } = [];

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetSlot(string kind, int index, string? itemId = null, int stack = 0, bool isEmpty = false, int ammo = 0)
            => _slots[(kind, index)] = new SlotState(itemId ?? "Empty", isEmpty || itemId is null, stack, 0, 0, ammo);

        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "inventory.list" => _slots.Select(kv => new
                {
                    kind = kv.Key.Kind,
                    slotIndex = kv.Key.Index,
                    itemId = kv.Value.ItemId,
                    isEmpty = kv.Value.IsEmpty,
                    stack = kv.Value.Stack,
                    durability = kv.Value.Durability,
                    maxDurability = kv.Value.MaxDurability,
                    ammoInMagazine = kv.Value.AmmoInMagazine,
                }).ToList(),
                "inventory.set" => ApplySet(payloadElement),
                "transmog.get" => new { visibility = Array.Empty<object>() },
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(element.Deserialize<TResponse>(JsonOptions)!);
        }

        private object? ApplySet(JsonElement payload)
        {
            if (!payload.TryGetProperty("edits", out var edits)) return null;
            var sent = new List<SentEdit>();
            foreach (var edit in edits.EnumerateArray())
            {
                var kind = edit.GetProperty("kind").GetString()!;
                var index = edit.GetProperty("slotIndex").GetInt32();
                var clear = edit.TryGetProperty("clear", out var clearProp) && clearProp.ValueKind == JsonValueKind.True;
                if (clear)
                {
                    _slots[(kind, index)] = new SlotState("Empty", true, 0, 0, 0);
                    sent.Add(new SentEdit(kind, index, Clear: true, null, null));
                    continue;
                }
                var itemId = edit.TryGetProperty("itemId", out var itemProp) && itemProp.ValueKind == JsonValueKind.String
                    ? itemProp.GetString()
                    : null;
                var stack = edit.TryGetProperty("stack", out var stackProp) && stackProp.ValueKind == JsonValueKind.Number
                    ? stackProp.GetInt32() : 0;
                var durability = edit.TryGetProperty("durability", out var durProp) && durProp.ValueKind == JsonValueKind.Number
                    ? durProp.GetDouble() : 0;
                var maxDurability = edit.TryGetProperty("maxDurability", out var maxDurProp) && maxDurProp.ValueKind == JsonValueKind.Number
                    ? maxDurProp.GetDouble() : 0;
                var ammo = edit.TryGetProperty("ammoInMagazine", out var ammoProp) && ammoProp.ValueKind == JsonValueKind.Number
                    ? ammoProp.GetInt32() : 0;
                _slots[(kind, index)] = new SlotState(itemId ?? "Empty", itemId is null, stack, durability, maxDurability, ammo);
                sent.Add(new SentEdit(kind, index, Clear: false, itemId, stack));
            }
            LastSetEdits = sent;
            return null;
        }

        private sealed record SlotState(string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability, int AmmoInMagazine = 0);
        public sealed record SentEdit(string Kind, int SlotIndex, bool? Clear, string? ItemId, int? Stack);
    }
}
