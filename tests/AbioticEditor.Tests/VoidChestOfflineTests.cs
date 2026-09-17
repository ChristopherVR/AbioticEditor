using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Offline counterpart of the live Void Chest fix (round 90): every placed Void Chest is one chest
/// in the game, whose real contents live in the world-wide <c>CustomInventoryMap</c> entry named
/// <c>Void</c> - the per-chest inventory each deployed entry carries is a decoy. The file session
/// must show and edit that shared entry through any Void Chest row, and keep every Void Chest's
/// name in step, exactly as the live editor does.
/// </summary>
public class VoidChestOfflineTests
{
    private static string FacilitySavePath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static WorldSaveSession Open() => new(WorldSaveReader.ReadFromFile(FacilitySavePath), FacilitySavePath);

    private static bool IsVoidChest(WorldContainer c)
        => c.Source == WorldContainerSource.Deployed && c.ClassName?.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase) == true;

    [Fact]
    public void Every_void_chest_row_shows_the_shared_void_entry()
    {
        if (!File.Exists(FacilitySavePath)) return;
        var session = Open();
        var pool = session.Containers.Single(c => c.Source == WorldContainerSource.Custom && c.Id == "Void");
        var chests = session.Containers.Where(IsVoidChest).ToList();
        Assert.NotEmpty(chests);
        foreach (var chest in chests)
        {
            Assert.Equal(pool.Inventories.Count, chest.Inventories.Count);
            for (var i = 0; i < pool.Inventories.Count; i++)
                Assert.Equal(pool.Inventories[i].Slots.Select(s => (s.ItemId, s.Count)), chest.Inventories[i].Slots.Select(s => (s.ItemId, s.Count)));
        }
    }

    [Fact]
    public async Task Editing_a_void_chest_slot_edits_the_shared_void_entry()
    {
        if (!File.Exists(FacilitySavePath)) return;
        var session = Open();
        IWorldContainersSession containers = session;
        var chest = session.Containers.First(IsVoidChest);
        var donor = session.Containers.Where(c => !IsVoidChest(c) && c.Id != "Void")
            .SelectMany(c => c.Inventories.SelectMany(inv => inv.Slots)).First(s => !s.IsEmpty);
        var placed = donor with { Index = 0 };

        Assert.True(await containers.TrySetContainerSlotAsync(chest.Source, chest.Id, 0, 0, placed, CancellationToken.None));

        var pool = session.Containers.Single(c => c.Source == WorldContainerSource.Custom && c.Id == "Void");
        Assert.Equal(donor.ItemId, pool.Inventories[0].Slots[0].ItemId);
        // And every other Void Chest row now shows it too - one chest, one set of contents.
        foreach (var other in session.Containers.Where(IsVoidChest))
            Assert.Equal(donor.ItemId, other.Inventories[0].Slots[0].ItemId);
    }

    [Fact]
    public void Renaming_one_void_chest_renames_every_void_chest()
    {
        if (!File.Exists(FacilitySavePath)) return;
        var session = Open();
        var chests = session.Containers.Where(IsVoidChest).ToList();
        Assert.NotEmpty(chests);

        Assert.True(session.TryRenameContainer(chests[0].Source, chests[0].Id, "One Chest"));

        Assert.All(session.Containers.Where(IsVoidChest), c => Assert.Equal("One Chest", c.Name));
        // An ordinary container's name is untouched by it.
        Assert.All(session.Containers.Where(c => !IsVoidChest(c)), c => Assert.NotEqual("One Chest", c.Name));
    }
}
