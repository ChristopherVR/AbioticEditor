using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
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
    public async Task Transfer_uses_one_authoritative_request_and_refreshes_both_containers()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("first", "Crate", 0, 0, 0);
        channel.SetContainer("second", "Crate", 0, 0, 0);
        channel.SetSlot("first", 0, "weapon", 1, ammo: 9);
        channel.SetSlot("second", 0, "food", 2);
        var session = await LiveContainersSession.ConnectAsync(new(channel));
        await session.TransferAsync(new(0, ContainerId: "first"), new(0, ContainerId: "second"));
        Assert.Equal(1, channel.WriteRequests);
        Assert.Equal("food", session.Containers[0].Inventories[0].Slots[0].ItemId);
        Assert.Equal(9, session.Containers[1].Inventories[0].Slots[0].AmmoInMagazine);
    }

    [Fact]
    public async Task Swap_sends_both_sides_in_one_request_and_refreshes_once()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetSlot("c1", 0, "first", 2);
        channel.SetSlot("c1", 1, "second", 3);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        var notifications = 0;
        session.Changed += () => notifications++;
        Assert.True(await session.TrySwapContainerSlotsAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, 1));
        Assert.Equal(1, channel.WriteRequests);
        Assert.Equal(1, notifications);
        Assert.Equal("second", session.Containers[0].Inventories[0].Slots[0].ItemId);
        Assert.Equal("first", session.Containers[0].Inventories[0].Slots[1].ItemId);
    }
    [Fact]
    public async Task Container_swap_preserves_ammo_and_instance_details()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetSlot("c1", 0, "first", 1, ammo: 7,
            details: new(25, "E_LiquidType::NewEnumerator13", true, "Custom", "identity", "Poster_Art"));
        channel.SetSlot("c1", 1);
        var session = await LiveContainersSession.ConnectAsync(new(channel));
        var original = session.Containers[0].Inventories[0].Slots[0];
        Assert.Equal(7, original.AmmoInMagazine);
        Assert.Equal("Custom", original.PlayerMadeString);
        Assert.True(await session.TrySwapContainerSlotsAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, 1));
        Assert.Equal(original with { Index = 1 }, session.Containers[0].Inventories[0].Slots[1]);
        Assert.True(session.Containers[0].Inventories[0].Slots[0].IsEmpty);
    }
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

        // Round 91: the periodic tick (this zero-argument overload) re-reads only the container
        // the tab has open, so the tab tells the session which one that is.
        session.WatchedContainerId = "c1";
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
        // Once for the immediate local apply; the background reconciling re-read may add another.
        Assert.True(raised >= 1, $"Changed was raised {raised} times");
        Assert.Equal("Item_Torch", session.Containers[0].Inventories[0].Slots[0].ItemId);
        Assert.Null(session.Status);
    }

    /// <summary>
    /// Regression test for the "Delete doesn't remove the item immediately - it stays for a good
    /// few seconds" live-mode bug: setting a slot to empty must clear it in
    /// <see cref="LiveContainersSession.Containers"/> the instant the awaited
    /// <c>containers.set</c> reply comes back, not after the <c>containers.get</c> re-read it fires
    /// afterwards as reconciliation. The gate below holds that follow-up read open for the whole
    /// assertion, so a regression that went back to awaiting it inline would hang instead of just
    /// happening to pass quickly.
    /// </summary>
    [Fact]
    public async Task Clearing_a_slot_updates_the_container_before_the_reconciling_get_completes()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetSlot("c1", 0, "Item_Rope", stack: 4);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        Assert.False(session.Containers[0].Inventories[0].Slots[0].IsEmpty);

        channel.ArmGetGate();
        var cleared = new InventoryItemSlot(0, null, 0, 0, 0, 0, 0, null, false, null, null);
        var applied = await session.TrySetContainerSlotAsync(
            AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, cleared);

        Assert.True(applied);
        Assert.True(session.Containers[0].Inventories[0].Slots[0].IsEmpty);

        channel.ReleaseGetGate();
    }

    // ---- round 91: writes and the periodic tick re-read ONE container, never the world ----

    [Fact]
    public async Task Setting_a_slot_re_reads_only_that_container_not_the_world()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetContainer("c2", "Crate", 5, 5, 5);
        channel.SetSlot("c1", 0, isEmpty: true);
        channel.SetSlot("c2", 0, "Item_Rope", 1);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        Assert.Equal(1, channel.ListRequests);

        var slot = new InventoryItemSlot(0, "Item_Torch", 1, 0, 0, 0, 0, null, false, null, null);
        Assert.True(await session.TrySetContainerSlotAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, slot));

        Assert.Equal(1, channel.ListRequests);
        Assert.Equal(1, channel.GetRequests);
        Assert.Equal("Item_Torch", session.Containers[0].Inventories[0].Slots[0].ItemId);
        // The other container is untouched and still in its place.
        Assert.Equal("c2", session.Containers[1].Id);
        Assert.Equal("Item_Rope", session.Containers[1].Inventories[0].Slots[0].ItemId);
    }

    [Fact]
    public async Task Transfer_re_reads_both_container_ends_only()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("first", "Crate", 0, 0, 0);
        channel.SetContainer("second", "Crate", 0, 0, 0);
        channel.SetContainer("third", "Crate", 0, 0, 0);
        channel.SetSlot("first", 0, "weapon", 1);
        channel.SetSlot("second", 0, isEmpty: true);
        var session = await LiveContainersSession.ConnectAsync(new(channel));
        await session.TransferAsync(new(0, ContainerId: "first"), new(0, ContainerId: "second"));
        Assert.Equal(1, channel.ListRequests);
        Assert.Equal(2, channel.GetRequests);
        Assert.True(session.Containers[0].Inventories[0].Slots[0].IsEmpty);
        Assert.Equal("weapon", session.Containers[1].Inventories[0].Slots[0].ItemId);
    }

    [Fact]
    public async Task The_periodic_tick_re_reads_the_watched_container_and_nothing_when_none_is_open()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetHealth("c1", 600, 600);
        channel.SetSlot("c1", 0, isEmpty: true);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        var raised = 0;
        session.Changed += () => raised++;

        // Nothing open: the tick costs the game nothing at all.
        await session.RefreshAsync();
        Assert.Equal(0, channel.GetRequests);
        Assert.Equal(1, channel.ListRequests);
        Assert.Equal(0, raised);

        // The player opens the container, then the game damages it and someone drops an item in.
        session.WatchedContainerId = "c1";
        channel.SetHealth("c1", 150, 600);
        channel.SetSlot("c1", 0, "Item_Bandage", 2);
        await session.RefreshAsync();

        Assert.Equal(1, channel.GetRequests);
        Assert.Equal(1, channel.ListRequests);
        Assert.Equal(1, raised);
        Assert.Equal(150, session.Containers[0].Health);
        Assert.Equal("Item_Bandage", session.Containers[0].Inventories[0].Slots[0].ItemId);
    }

    [Fact]
    public async Task The_explicit_refresh_is_still_the_full_world_scan()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        channel.SetContainer("c2", "Crate", 1, 1, 1);
        await session.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, channel.ListRequests);
        Assert.Equal(2, session.Containers.Count);
    }

    [Fact]
    public async Task Refreshing_one_void_chest_mirrors_its_contents_and_name_onto_every_void_chest()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("void1", "Deployed_StorageCrate_Void_C", 0, 0, 0);
        channel.SetContainer("void2", "Deployed_StorageCrate_Void_C", 9, 9, 9);
        channel.SetContainer("plain", "Deployed_StorageCrate_Makeshift_C", 1, 1, 1);
        channel.SetSlot("void1", 0, isEmpty: true);
        channel.SetSlot("void2", 0, isEmpty: true);
        channel.SetSlot("plain", 0, isEmpty: true);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));

        var slot = new InventoryItemSlot(0, "Item_Carrot", 3, 0, 0, 0, 0, null, false, null, null);
        Assert.True(await session.TrySetContainerSlotAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "void1", 0, 0, slot));
        Assert.True(await session.TryRenameContainerAsync(AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "void1", "Pool"));

        Assert.Equal(1, channel.ListRequests);
        foreach (var id in new[] { "void1", "void2" })
        {
            var chest = session.Containers.Single(c => c.Id == id);
            Assert.Equal("Item_Carrot", chest.Inventories[0].Slots[0].ItemId);
            Assert.Equal("Pool", chest.Name);
        }
        var ordinary = session.Containers.Single(c => c.Id == "plain");
        Assert.True(ordinary.Inventories[0].Slots[0].IsEmpty);
        Assert.Null(ordinary.Name);
    }

    [Fact]
    public async Task Refreshing_a_container_the_game_no_longer_has_drops_it_and_stops_watching_it()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetContainer("c2", "Crate", 1, 1, 1);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        session.WatchedContainerId = "c1";
        var raised = 0;
        session.Changed += () => raised++;

        channel.RemoveContainer("c1");
        await session.RefreshAsync();

        Assert.Equal(1, raised);
        Assert.Null(session.WatchedContainerId);
        Assert.Equal("c2", Assert.Single(session.Containers).Id);
        // A second tick with nothing watched is free.
        await session.RefreshAsync();
        Assert.Equal(1, channel.GetRequests);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Setting_a_container_coating_sends_setcomplete_and_zeroes_on_clear()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        var metadata = new InventoryInstanceMetadata([new("EDynamicProperty::XP", 12)], ["Item.Weapon"]);
        channel.SetSlot("c1", 0, "weapon_test", 1, details: new(InstanceMetadata: metadata));
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        Assert.True(session.SupportsCompleteItemWrites);

        var slot = session.Containers[0].Inventories[0].Slots[0];
        Assert.Null(slot.CoatingIndex);
        var applied = await session.TrySetContainerSlotAsync(
            AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, slot with { CoatingIndex = 4, CoatingDurability = 80 });
        Assert.True(applied);
        Assert.Equal("containers.setcomplete", channel.LastCommand);
        var coated = session.Containers[0].Inventories[0].Slots[0];
        Assert.Equal(4, coated.CoatingIndex);
        Assert.Equal(80, coated.CoatingDurability);
        Assert.Equal(12, coated.InstanceMetadata!.DynamicProperties.Single(p => p.Key.EndsWith("::XP", StringComparison.Ordinal)).Value);

        await session.TrySetContainerSlotAsync(
            AbioticEditor.Core.WorldSaves.WorldContainerSource.Live, "c1", 0, 0, coated with { CoatingIndex = -1, CoatingDurability = 0 });
        var cleared = session.Containers[0].Inventories[0].Slots[0];
        Assert.Equal(-1, cleared.CoatingIndex);
        Assert.Equal(0, cleared.CoatingDurability);
        Assert.Contains(cleared.InstanceMetadata!.DynamicProperties, p => p.Key.EndsWith("::WeaponCoating", StringComparison.Ordinal) && p.Value == -1);
        Assert.Contains(cleared.InstanceMetadata!.DynamicProperties, p => p.Key.EndsWith("::CoatingDurability", StringComparison.Ordinal) && p.Value == 0);
    }

    [Fact]
    public async Task SupportsCompleteItemWrites_stays_false_for_an_agent_that_never_reports_instance_metadata()
    {
        var channel = new FakeContainersChannel();
        channel.SetContainer("c1", "Locker", 0, 0, 0);
        channel.SetSlot("c1", 0, "weapon_test", 1);
        var session = await LiveContainersSession.ConnectAsync(new LiveContainersChannel(channel));
        Assert.False(session.SupportsCompleteItemWrites);

        await session.RefreshAsync();
        Assert.False(session.SupportsCompleteItemWrites);
    }

    private sealed class FakeContainersChannel : ILiveGameChannel
    {
        public int WriteRequests { get; private set; }
        public string? LastCommand { get; private set; }
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, (string Label, double X, double Y, double Z)> _containers = new(StringComparer.Ordinal);
        private readonly Dictionary<(string ContainerId, int SlotIndex), SlotState> _slots = new();

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetContainer(string id, string label, double x, double y, double z) => _containers[id] = (label, x, y, z);

        public void SetSlot(string containerId, int slotIndex, string? itemId = null, int stack = 0, bool isEmpty = false,
            int ammo = 0, LiveItemDetails? details = null)
            => _slots[(containerId, slotIndex)] = new SlotState(itemId ?? "Empty", isEmpty || itemId is null, stack, 0, 0, ammo, details);

        // Lets a test hold "containers.get" open to prove a caller does not (and, after
        // LiveContainersSession's background-reconciliation fix, no longer needs to) wait on it.
        private TaskCompletionSource? _getGate;
        public void ArmGetGate() => _getGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseGetGate() => _getGate?.TrySetResult();

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "containers.get" && _getGate is { } gate) await gate.Task.ConfigureAwait(false);
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "containers.list" => List(),
                "containers.get" => GetOne(payloadElement),
                "containers.set" or "containers.setfull" or "containers.setcomplete" => ApplySet(command, payloadElement),
                "containers.rename" => ApplyRename(payloadElement),
                "inventory.transfer" => ApplyTransfer(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return element.Deserialize<TResponse>(JsonOptions)!;
        }

        public int ListRequests { get; private set; }
        public int GetRequests { get; private set; }
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (double Health, double MaxHealth)> _health = new(StringComparer.Ordinal);

        public void SetHealth(string id, double health, double maxHealth) => _health[id] = (health, maxHealth);
        public void RemoveContainer(string id) => _containers.Remove(id);

        private object Row(string id)
        {
            var value = _containers[id];
            return new
            {
                id,
                label = value.Label,
                x = value.X,
                y = value.Y,
                z = value.Z,
                slots = _slots.Where(s => s.Key.ContainerId == id).Select(s => new
                {
                    slotIndex = s.Key.SlotIndex,
                    itemId = s.Value.ItemId,
                    isEmpty = s.Value.IsEmpty,
                    stack = s.Value.Stack,
                    durability = s.Value.Durability,
                    maxDurability = s.Value.MaxDurability,
                    ammoInMagazine = s.Value.Ammo,
                    details = s.Value.Details,
                }).ToList(),
                health = _health.TryGetValue(id, out var h) ? h.Health : (double?)null,
                maxHealth = _health.TryGetValue(id, out var m) ? m.MaxHealth : (double?)null,
                name = _names.TryGetValue(id, out var n) ? n : null,
            };
        }

        private object GetOne(JsonElement payload)
        {
            GetRequests++;
            var id = payload.GetProperty("id").GetString()!;
            if (!_containers.ContainsKey(id)) throw new LiveAgentException("container not found (it may have been unloaded or destroyed)");
            return new { container = Row(id), isHost = true };
        }

        private object? ApplyRename(JsonElement payload)
        {
            WriteRequests++;
            var id = payload.GetProperty("id").GetString()!;
            var name = payload.GetProperty("name").GetString() ?? "";
            // The mod fans a Void Chest's name out to every Void Chest (see main.lua).
            var targets = _containers[id].Label.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase)
                ? _containers.Where(c => c.Value.Label.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase)).Select(c => c.Key).ToList()
                : [id];
            foreach (var target in targets) _names[target] = name;
            return null;
        }

        private object? ApplyTransfer(JsonElement payload)
        {
            WriteRequests++;
            var first = payload.GetProperty("first").Deserialize<LiveInventoryEndpoint>(JsonOptions)!;
            var second = payload.GetProperty("second").Deserialize<LiveInventoryEndpoint>(JsonOptions)!;
            var firstKey = (first.ContainerId!, first.SlotIndex);
            var secondKey = (second.ContainerId!, second.SlotIndex);
            (_slots[firstKey], _slots[secondKey]) = (_slots[secondKey], _slots[firstKey]);
            return null;
        }

        private object? ApplySet(string command, JsonElement payload)
        {
            WriteRequests++;
            LastCommand = command;
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
                _slots[(containerId, slotIndex)] = new SlotState(itemId ?? "Empty", itemId is null, stack, 0, 0,
                    edit.TryGetProperty("ammoInMagazine", out var ammo) && ammo.ValueKind == JsonValueKind.Number ? ammo.GetInt32() : 0,
                    edit.TryGetProperty("details", out var details) ? details.Deserialize<LiveItemDetails>(JsonOptions) : null);
            }
            return null;
        }

        private object List()
        {
            ListRequests++;
            return new { containers = _containers.Keys.Select(Row).ToList(), isHost = true };
        }

        private sealed record SlotState(string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability,
            int Ammo = 0, LiveItemDetails? Details = null);
    }
}
