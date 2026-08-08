using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>Cross-save pet movement: carried-pet reading, world<->player transfer, catalog mapping.</summary>
public class PetTransferTests
{
    private static string? FixturesRoot()
    {
        var seed = Fixtures.ServerWorldsDir ?? Fixtures.CascadeDir ?? Fixtures.ClientSavedDir;
        if (seed is null) return null;
        var dir = new DirectoryInfo(seed);
        while (dir is not null && !string.Equals(dir.Name, "fixtures", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static string? FindPlayerWithCarriedPet()
    {
        var root = FixturesRoot();
        if (root is null) return null;
        foreach (var f in Directory.EnumerateFiles(root, "Player_*.sav", SearchOption.AllDirectories))
        {
            try { if (PlayerSaveReader.ReadFromFile(f).CarriedPets.Count > 0) return f; } catch { }
        }
        return null;
    }

    private static string? FindWorldWithPets()
    {
        var root = FixturesRoot();
        if (root is null) return null;
        foreach (var f in Directory.EnumerateFiles(root, "WorldSave_*.sav", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f).Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase)) continue;
            try { if (WorldSaveReader.ReadFromFile(f).Pets.Count > 0) return f; } catch { }
        }
        return null;
    }

    [Fact]
    public void Catalog_maps_item_rows_to_friendly_names_and_classes()
    {
        Assert.NotEmpty(PetItemCatalog.Items);
        Assert.All(PetItemCatalog.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Friendly)));
        Assert.True(PetItemCatalog.IsPetItem("Skink_Magma_Crafted"));
        Assert.False(PetItemCatalog.IsPetItem("knife_super"));

        // The magma skink weapon item bridges to the Magma Skink creature class.
        var cls = PetItemCatalog.NpcClassFor("Skink_Magma_Crafted");
        Assert.NotNull(cls);
        Assert.Equal(PetCategory.Skink, PetCatalog.Categorize(cls));

        // And back: the class resolves to a (held-preferred) pet item row.
        var row = PetItemCatalog.ItemRowFor(cls);
        Assert.NotNull(row);
        Assert.Equal("Magma Skink", PetItemCatalog.FriendlyName(row));
    }

    [Fact]
    public void Carried_pets_are_read_from_player_saves()
    {
        var player = FindPlayerWithCarriedPet();
        if (player is null) return; // fixtures absent: skip

        var pets = PlayerSaveReader.ReadFromFile(player).CarriedPets;
        Assert.NotEmpty(pets);
        Assert.All(pets, p => Assert.True(PetItemCatalog.IsPetItem(p.ItemRow)));
        Assert.All(pets, p => Assert.True(p.Level >= 0 && p.Level <= PetCatalog.MaxLevel));
    }

    [Fact]
    public void WorldToPlayer_moves_a_pet_and_round_trips()
    {
        var worldSrc = FindWorldWithPets();
        var playerSrc = FindPlayerWithCarriedPet();
        if (worldSrc is null || playerSrc is null) return;

        var dir = Directory.CreateTempSubdirectory("pet-w2p");
        try
        {
            var worldCopy = Path.Combine(dir.FullName, Path.GetFileName(worldSrc));
            var playerCopy = Path.Combine(dir.FullName, Path.GetFileName(playerSrc));
            File.Copy(worldSrc, worldCopy);
            File.Copy(playerSrc, playerCopy);

            var world = WorldSaveReader.ReadFromFile(worldCopy);
            var player = PlayerSaveReader.ReadFromFile(playerCopy);
            var worldCountBefore = world.Pets.Count;
            var playerCountBefore = player.CarriedPets.Count;
            var pet = world.Pets[0];

            // Pick whichever inventory array actually has a free slot in this fixture.
            PetSlotKind? kind = null;
            foreach (var k in new[] { PetSlotKind.Hotbar, PetSlotKind.Main, PetSlotKind.Equipment })
            {
                if (PlayerSaveWriter.FindFreeSlot(player, k) >= 0) { kind = k; break; }
            }
            if (kind is null) return; // fully packed save: nothing to test

            var result = PetTransfer.WorldToPlayer(world, pet.Id, player, kind.Value);
            Assert.True(result.Ok, result.Message);

            WorldSaveWriter.WriteToFile(world, worldCopy);
            PlayerSaveWriter.WriteToFile(player, playerCopy);

            var worldBack = WorldSaveReader.ReadFromFile(worldCopy);
            var playerBack = PlayerSaveReader.ReadFromFile(playerCopy);
            Assert.Equal(worldCountBefore - 1, worldBack.Pets.Count);
            Assert.DoesNotContain(worldBack.Pets, p => p.Id == pet.Id);
            Assert.Equal(playerCountBefore + 1, playerBack.CarriedPets.Count);
            // XP/level preserved on the new carried pet.
            Assert.Contains(playerBack.CarriedPets, c => c.Xp == pet.Xp && c.Slot == kind.Value);
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary>A region save that carries story NPCs but has never held a pet.</summary>
    private static string? FindWorldWithNpcsButNoPets()
    {
        var root = FixturesRoot();
        if (root is null) return null;
        foreach (var f in Directory.EnumerateFiles(root, "WorldSave_*.sav", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f).Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var world = WorldSaveReader.ReadFromFile(f);
                if (world.Pets.Count == 0 && world.Npcs.Count > 0) return f;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// A companion can be sent to a region that has never had one in it.
    /// </summary>
    /// <remarks>
    /// The game leaves the pet map out of a save entirely until something is written into it, so
    /// all but the main facility save lack one. The editor used to refuse those at the last
    /// moment: the send looked like it worked, and saving the receiving world then failed with
    /// "could not place pet". The map is now created from the story-NPC map's own shape.
    /// </remarks>
    [Fact]
    public void PlayerToWorld_places_a_pet_in_a_region_that_has_never_had_one()
    {
        var worldSrc = FindWorldWithNpcsButNoPets();
        var playerSrc = FindPlayerWithCarriedPet();
        if (worldSrc is null || playerSrc is null) return; // fixtures absent: skip

        var dir = Directory.CreateTempSubdirectory("pet-p2w-fresh");
        try
        {
            var worldCopy = Path.Combine(dir.FullName, Path.GetFileName(worldSrc));
            var playerCopy = Path.Combine(dir.FullName, Path.GetFileName(playerSrc));
            File.Copy(worldSrc, worldCopy);
            File.Copy(playerSrc, playerCopy);

            var world = WorldSaveReader.ReadFromFile(worldCopy);
            var player = PlayerSaveReader.ReadFromFile(playerCopy);
            Assert.Empty(world.Pets);
            var carried = player.CarriedPets[0];
            var expectedClass = PetItemCatalog.NpcClassFor(carried.ItemRow);

            var result = PetTransfer.PlayerToWorld(player, carried.Slot, carried.Index, world, 11, 22, 33);
            Assert.True(result.Ok, result.Message);

            WorldSaveWriter.WriteToFile(world, worldCopy);

            // Re-read from disk: the created map has to survive a full serialize/parse round trip,
            // which is the part a purely in-memory check would not prove.
            var worldBack = WorldSaveReader.ReadFromFile(worldCopy);
            var placed = Assert.Single(worldBack.Pets);
            Assert.Equal(expectedClass, placed.NpcClass);
            Assert.True(Math.Abs(placed.X - 11) < 1 && Math.Abs(placed.Y - 22) < 1);
            // Everything else in the save must be untouched by the new map.
            Assert.Equal(WorldSaveReader.ReadFromFile(worldSrc).Npcs.Count, worldBack.Npcs.Count);

            // The one thing that cannot come across: the entry was cloned from a story NPC, and
            // a pet keeps its experience in a list story NPCs do not carry, so there is nothing
            // in this world to copy that list's shape from. The result must say so rather than
            // claim a level it did not write.
            Assert.Equal(0, placed.Xp);
            Assert.Contains("starts again at level 1", result.Message, StringComparison.Ordinal);
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void PlayerToWorld_places_a_pet_and_round_trips()
    {
        var worldSrc = FindWorldWithPets();
        var playerSrc = FindPlayerWithCarriedPet();
        if (worldSrc is null || playerSrc is null) return;

        var dir = Directory.CreateTempSubdirectory("pet-p2w");
        try
        {
            var worldCopy = Path.Combine(dir.FullName, Path.GetFileName(worldSrc));
            var playerCopy = Path.Combine(dir.FullName, Path.GetFileName(playerSrc));
            File.Copy(worldSrc, worldCopy);
            File.Copy(playerSrc, playerCopy);

            var world = WorldSaveReader.ReadFromFile(worldCopy);
            var player = PlayerSaveReader.ReadFromFile(playerCopy);
            var worldCountBefore = world.Pets.Count;
            var carried = player.CarriedPets[0];
            var expectedClass = PetItemCatalog.NpcClassFor(carried.ItemRow);

            var result = PetTransfer.PlayerToWorld(player, carried.Slot, carried.Index, world, 123, 456, 789);
            Assert.True(result.Ok, result.Message);

            WorldSaveWriter.WriteToFile(world, worldCopy);
            PlayerSaveWriter.WriteToFile(player, playerCopy);

            var worldBack = WorldSaveReader.ReadFromFile(worldCopy);
            var playerBack = PlayerSaveReader.ReadFromFile(playerCopy);
            Assert.Equal(worldCountBefore + 1, worldBack.Pets.Count);
            Assert.DoesNotContain(playerBack.CarriedPets, c => c.Slot == carried.Slot && c.Index == carried.Index);
            // The placed pet has the mapped class, the preserved XP, and the requested location.
            Assert.Contains(worldBack.Pets, p => p.NpcClass == expectedClass && p.Xp == carried.Xp
                && Math.Abs(p.X - 123) < 1 && Math.Abs(p.Y - 456) < 1);
        }
        finally { dir.Delete(recursive: true); }
    }
}
