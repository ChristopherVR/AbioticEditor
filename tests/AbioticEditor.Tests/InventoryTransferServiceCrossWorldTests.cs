using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="InventoryTransferService.TryMoveContainerToContainer"/> is what makes the
/// "Transfer Items" tool (<c>/transfer-items</c>) work: it moves one item between two
/// <see cref="WorldSaveSession"/> instances that need not come from the same save folder, or
/// even the same file. Two independently-loaded sessions built from the SAME fixture file stand
/// in for "two separate world saves" here - the service has no notion of where a session's
/// bytes came from, only that it is a distinct in-memory session.
/// </summary>
public sealed class InventoryTransferServiceCrossWorldTests(ITestOutputHelper output)
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    [Fact]
    public void Moving_an_item_empties_the_source_and_fills_the_destination_across_independent_sessions()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilityPath)) return;

        // Two completely independent reads/sessions, exactly as the transfer page builds one
        // per side after each is picked and loaded on its own.
        var sourceData = WorldSaveReader.ReadFromFile(FacilityPath);
        var destData = WorldSaveReader.ReadFromFile(FacilityPath);
        var source = new WorldSaveSession(sourceData, FacilityPath);
        var destination = new WorldSaveSession(destData, FacilityPath);

        var (sourceContainer, sourceSlotIndex) = FindOccupiedSlot(source)
            ?? throw new InvalidOperationException("fixture has no occupied container slot to move");
        var (destContainer, destSlotIndex) = FindEmptySlot(destination, exclude: sourceContainer.Id)
            ?? throw new InvalidOperationException("fixture has no empty container slot to receive the move");

        Assert.True(source.TryGetContainerSlot(sourceContainer.Source, sourceContainer.Id, 0, sourceSlotIndex, out var movingSlot));
        output.WriteLine($"Moving '{movingSlot.ItemId}' from {sourceContainer.Id}[{sourceSlotIndex}] to {destContainer.Id}[{destSlotIndex}]");

        var moved = InventoryTransferService.TryMoveContainerToContainer(
            source, sourceContainer.Source, sourceContainer.Id, 0, sourceSlotIndex,
            destination, destContainer.Source, destContainer.Id, 0, destSlotIndex);

        Assert.True(moved);

        Assert.True(source.TryGetContainerSlot(sourceContainer.Source, sourceContainer.Id, 0, sourceSlotIndex, out var afterSource));
        Assert.True(afterSource.IsEmpty);

        Assert.True(destination.TryGetContainerSlot(destContainer.Source, destContainer.Id, 0, destSlotIndex, out var afterDest));
        Assert.False(afterDest.IsEmpty);
        Assert.Equal(movingSlot.ItemId, afterDest.ItemId);
        Assert.Equal(movingSlot.Count, afterDest.Count);
        Assert.Equal(movingSlot.VariantRowName, afterDest.VariantRowName);

        // Each side is a fully independent staged session: only the two touched sessions are
        // dirty, and each would keep its own .bak on its own SaveAsync.
        Assert.True(source.IsDirty);
        Assert.True(destination.IsDirty);
    }

    [Fact]
    public void Moving_into_an_occupied_slot_fails_and_changes_nothing()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilityPath)) return;

        var sourceData = WorldSaveReader.ReadFromFile(FacilityPath);
        var destData = WorldSaveReader.ReadFromFile(FacilityPath);
        var source = new WorldSaveSession(sourceData, FacilityPath);
        var destination = new WorldSaveSession(destData, FacilityPath);

        var (sourceContainer, sourceSlotIndex) = FindOccupiedSlot(source)
            ?? throw new InvalidOperationException("fixture has no occupied container slot to move");
        // Deliberately target another occupied slot (or the same one) so the destination is
        // never empty - the move must refuse rather than overwrite an existing item.
        var (destContainer, destSlotIndex) = FindOccupiedSlot(destination)
            ?? throw new InvalidOperationException("fixture has no second occupied slot");

        var moved = InventoryTransferService.TryMoveContainerToContainer(
            source, sourceContainer.Source, sourceContainer.Id, 0, sourceSlotIndex,
            destination, destContainer.Source, destContainer.Id, 0, destSlotIndex);

        Assert.False(moved);
        Assert.False(source.IsDirty);
        Assert.False(destination.IsDirty);
    }

    private static (WorldContainer Container, int SlotIndex)? FindOccupiedSlot(WorldSaveSession session)
    {
        foreach (var container in session.Containers)
        {
            if (container.Inventories.Count == 0) continue;
            var slots = container.Inventories[0].Slots;
            for (var i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty) return (container, i);
        }
        return null;
    }

    private static (WorldContainer Container, int SlotIndex)? FindEmptySlot(WorldSaveSession session, string exclude)
    {
        foreach (var container in session.Containers)
        {
            if (string.Equals(container.Id, exclude, StringComparison.Ordinal)) continue;
            if (container.Inventories.Count == 0) continue;
            var slots = container.Inventories[0].Slots;
            for (var i = 0; i < slots.Count; i++)
                if (slots[i].IsEmpty) return (container, i);
        }
        return null;
    }
}
