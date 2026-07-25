using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class InventoryTransferSessionTests
{
    [Fact]
    public void Player_sort_and_swap_are_staged_and_revertible()
    {
        var session = OpenPlayer();
        Assert.True(session.Backpack.Count >= 2);
        var first = session.Backpack[0].ToInventorySlot();
        var second = session.Backpack[1].ToInventorySlot();
        session.TrySetInventorySlot(PlayerInventoryArea.Backpack, 0, first with { ItemId = "ZZZ_Test", Count = 1 });
        session.TrySetInventorySlot(PlayerInventoryArea.Backpack, 1, second with { ItemId = "AAA_Test", Count = 2 });

        session.SortInventorySlots(PlayerInventoryArea.Backpack);
        var sortedIds = session.Backpack.Where(slot => !slot.IsEmpty).Select(slot => slot.ItemId!).ToArray();
        Assert.Equal(sortedIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase), sortedIds);
        var sortedFirst = session.Backpack[0].ItemId;
        var sortedSecond = session.Backpack[1].ItemId;
        Assert.True(session.TrySwapInventorySlots(PlayerInventoryArea.Backpack, 0, 1));
        Assert.Equal(sortedSecond, session.Backpack[0].ItemId);
        Assert.Equal(sortedFirst, session.Backpack[1].ItemId);
        Assert.True(session.IsDirty);

        session.Revert();
        Assert.Equal(first, session.Backpack[0].ToInventorySlot());
        Assert.Equal(second, session.Backpack[1].ToInventorySlot());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Player_and_world_container_swap_is_staged_in_both_sessions_and_revertible()
    {
        var player = OpenPlayer();
        var world = OpenWorld();
        var target = world.Containers.SelectMany(container => container.Inventories.Select((inventory, inventoryIndex) => (container, inventory, inventoryIndex)))
            .First(candidate => candidate.inventory.Slots.Count > 0);
        var container = target.container;
        var inventoryIndex = target.inventoryIndex;
        var containerSlotIndex = target.inventory.Slots[0].Index;
        Assert.True(player.TryGetInventorySlot(PlayerInventoryArea.Backpack, 0, out var originalPlayer));
        Assert.True(world.TryGetContainerSlot(container.Source, container.Id, inventoryIndex, containerSlotIndex, out var originalWorld));

        Assert.True(InventoryTransferService.TrySwapPlayerAndContainer(player, PlayerInventoryArea.Backpack, 0,
            world, container.Source, container.Id, inventoryIndex, containerSlotIndex));
        Assert.Equal(originalWorld.ItemId, player.Backpack[0].ItemId);
        Assert.True(world.TryGetContainerSlot(container.Source, container.Id, inventoryIndex, containerSlotIndex, out var transferred));
        Assert.Equal(originalPlayer.ItemId, transferred.ItemId);
        Assert.True(player.IsDirty);
        Assert.True(world.IsDirty);

        player.Revert();
        world.Revert();
        Assert.Equal(originalPlayer, player.Backpack[0].ToInventorySlot());
        Assert.True(world.TryGetContainerSlot(container.Source, container.Id, inventoryIndex, containerSlotIndex, out var reverted));
        Assert.Equal(originalWorld, reverted);
    }

    [Fact]
    public void Transmog_slots_use_the_same_staged_swap_sort_and_revert_contract()
    {
        var session = OpenPlayer();
        Assert.True(session.Transmog.Count >= 2);
        var first = session.Transmog[0].ToInventorySlot();
        var second = session.Transmog[1].ToInventorySlot();
        Assert.True(session.TrySetInventorySlot(PlayerInventoryArea.Transmog, 0, first with { ItemId = "ZZZ_Cosmetic", Count = 1 }));
        Assert.True(session.TrySetInventorySlot(PlayerInventoryArea.Transmog, 1, second with { ItemId = "AAA_Cosmetic", Count = 1 }));

        session.SortInventorySlots(PlayerInventoryArea.Transmog);
        Assert.Equal("AAA_Cosmetic", session.Transmog[0].ItemId);
        Assert.True(session.TrySwapInventorySlots(PlayerInventoryArea.Transmog, 0, 1));
        Assert.Equal("ZZZ_Cosmetic", session.Transmog[0].ItemId);

        session.Revert();
        Assert.Equal(first, session.Transmog[0].ToInventorySlot());
        Assert.Equal(second, session.Transmog[1].ToInventorySlot());
    }

    [Fact]
    public void Player_slots_can_swap_between_inventory_areas_and_revert()
    {
        var session = OpenPlayer();
        var equipment = session.Equipment[0];
        var backpack = session.Backpack[0];
        var originalEquipment = equipment.ToInventorySlot();
        var originalBackpack = backpack.ToInventorySlot();

        Assert.True(session.TrySetInventorySlot(PlayerInventoryArea.Equipment, equipment.Index,
            originalEquipment with { ItemId = "CrossArea_Equipment", Count = 1 }));
        Assert.True(session.TrySetInventorySlot(PlayerInventoryArea.Backpack, backpack.Index,
            originalBackpack with { ItemId = "CrossArea_Backpack", Count = 2 }));
        Assert.True(session.TrySwapInventorySlots(
            PlayerInventoryArea.Equipment, equipment.Index,
            PlayerInventoryArea.Backpack, backpack.Index));

        Assert.Equal("CrossArea_Backpack", equipment.ItemId);
        Assert.Equal(2, equipment.Count);
        Assert.Equal(equipment.Index, equipment.ToInventorySlot().Index);
        Assert.Equal("CrossArea_Equipment", backpack.ItemId);
        Assert.Equal(1, backpack.Count);
        Assert.Equal(backpack.Index, backpack.ToInventorySlot().Index);

        session.Revert();
        Assert.Equal(originalEquipment, equipment.ToInventorySlot());
        Assert.Equal(originalBackpack, backpack.ToInventorySlot());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Cross_area_swap_rejects_missing_slots_without_partial_changes()
    {
        var session = OpenPlayer();
        var original = session.Backpack[0].ToInventorySlot();

        Assert.False(session.TrySwapInventorySlots(
            PlayerInventoryArea.Backpack, session.Backpack[0].Index,
            PlayerInventoryArea.Hotbar, int.MaxValue));

        Assert.Equal(original, session.Backpack[0].ToInventorySlot());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Ground_pickup_stages_both_sessions_and_reverts()
    {
        var player = OpenPlayer();
        var world = OpenWorld();
        var ground = world.DroppedItems.First(item => !item.Slot.IsEmpty);
        var target = player.Backpack.First(slot => slot.IsEmpty);
        var originalTarget = target.ToInventorySlot();

        Assert.True(InventoryTransferService.TryPickUpDroppedItem(
            player, PlayerInventoryArea.Backpack, target.Index, world, ground.Id));
        Assert.Equal(ground.Slot.ItemId, target.ItemId);
        Assert.DoesNotContain(world.DroppedItems, item => item.Id == ground.Id);
        Assert.True(player.IsDirty);
        Assert.True(world.IsDirty);

        player.Revert();
        world.Revert();
        Assert.Equal(originalTarget, target.ToInventorySlot());
        Assert.Contains(world.DroppedItems, item => item.Id == ground.Id);
    }

    [Fact]
    public void Ground_drop_stages_both_sessions_and_reverts()
    {
        var player = OpenPlayer();
        var world = OpenWorld();
        var source = player.Backpack.First(slot => !slot.IsEmpty);
        var original = source.ToInventorySlot();

        Assert.True(InventoryTransferService.TryDropPlayerSlot(
            player, PlayerInventoryArea.Backpack, source.Index, world, 10, 20, 30, out var pendingId));
        Assert.True(source.IsEmpty);
        Assert.Contains(world.DroppedItems, item => item.Id == pendingId && item.Slot.ItemId == original.ItemId);

        player.Revert();
        world.Revert();
        Assert.Equal(original, source.ToInventorySlot());
        Assert.DoesNotContain(world.DroppedItems, item => item.Id == pendingId);
    }

    [Fact]
    public async Task Ground_drop_save_uses_writer_and_becomes_the_new_baseline()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var sourcePath = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        var directory = Directory.CreateTempSubdirectory("abiotic-ground-drop-");
        var path = Path.Combine(directory.FullName, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, path);
        try
        {
            var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(path), path);
            var count = session.DroppedItems.Count;
            var template = session.DroppedItems.First(item => !item.Slot.IsEmpty).Slot;
            Assert.True(session.TryAddDroppedItem(template with { Count = 7 }, 100, 200, 300, out _));

            await session.SaveAsync();

            Assert.False(session.IsDirty);
            Assert.Equal(count + 1, session.DroppedItems.Count);
            var reread = WorldSaveReader.ReadFromFile(path);
            Assert.Contains(reread.DroppedItems, item => item.Slot.ItemId == template.ItemId && item.Slot.Count == 7
                && item.X == 100 && item.Y == 200 && item.Z == 300);
            Assert.True(File.Exists(path + ".bak"));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static PlayerSaveSession OpenPlayer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        return new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
    }

    private static WorldSaveSession OpenWorld()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        return new WorldSaveSession(WorldSaveReader.ReadFromFile(path), path);
    }
}
