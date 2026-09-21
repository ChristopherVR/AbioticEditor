using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Cross-save COMPANIONS flow for the web editor: pet beds are discovered read-only from the
/// sibling world saves of an open player save, and a send stages into a world session that
/// only writes on save (with a .bak), while the player removal stages until the player save.
/// </summary>
public sealed class WebCompanionCrossSaveTests
{
    [Fact]
    public void Catalog_add_refuses_summons_and_invalid_coordinates_without_staging()
    {
        using var world = CopyCascadeWorld();
        var path = Path.Combine(world.Path, "WorldSave_Facility.sav");
        var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(path), path);
        var summon = PetCatalog.Curated.First(candidate => !candidate.IsEditable);
        Assert.False(session.TryAddCatalogPet(summon.ClassPath, null, 1, 2, 3, out _));
        var companion = PetCatalog.Curated.First(candidate => candidate.IsEditable);
        Assert.False(session.TryAddCatalogPet(companion.ClassPath, null, double.NaN, 2, 3, out _));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Catalog_adds_are_staged_until_the_matching_save()
    {
        using var world = CopyCascadeWorld();
        var playerPath = FindPlayer(world.Path);
        var facilityPath = Path.Combine(world.Path, "WorldSave_Facility.sav");
        var playerBytes = File.ReadAllBytes(playerPath);
        var worldBytes = File.ReadAllBytes(facilityPath);

        var playerSession = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(playerPath), playerPath);
        Assert.True(playerSession.TryAddCatalogPet("pest", PetSlotKind.Equipment, "Sparky", out var playerMessage), playerMessage);
        Assert.True(playerSession.IsDirty);
        Assert.Equal(playerBytes, File.ReadAllBytes(playerPath));
        await playerSession.SaveAsync();
        var playerPet = Assert.Single(PlayerSaveReader.ReadFromFile(playerPath).CarriedPets);
        Assert.Equal("Sparky", playerPet.Name);

        var worldSession = await new SiblingWorldBedService(new DesktopSaveFileSystem()).GetOrLoadSessionAsync(facilityPath);
        var variant = PetCatalog.Curated.First(candidate => candidate.IsEditable);
        Assert.True(worldSession.TryAddCatalogPet(variant.ClassPath, "Worldy", 1, 2, 3, out var worldMessage), worldMessage);
        Assert.True(worldSession.IsDirty);
        Assert.Equal(worldBytes, File.ReadAllBytes(facilityPath));
        await worldSession.SaveAsync();
        var added = Assert.Single(WorldSaveReader.ReadFromFile(facilityPath).Pets, pet => pet.CustomName == "Worldy");
        Assert.False(added.IsDead);
        Assert.True(added.TotalHealth > 0);
        Assert.Equal(1, added.X);
        Assert.Equal(2, added.Y);
        Assert.Equal(3, added.Z);
    }

    [Fact]
    public async Task Catalog_add_creates_a_pet_map_entry_when_the_world_has_only_npcs()
    {
        using var world = CopyCascadeWorld();
        var facilityPath = Path.Combine(world.Path, "WorldSave_Facility.sav");
        var data = WorldSaveReader.ReadFromFile(facilityPath);
        Assert.NotEmpty(data.Pets);
        Assert.NotEmpty(data.Npcs);
        foreach (var pet in data.Pets) Assert.True(WorldSaveWriter.RemovePet(data, pet.Id));
        WorldSaveWriter.WriteToFile(data, facilityPath);
        File.Delete(facilityPath + ".bak");

        var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(facilityPath), facilityPath);
        var variant = PetCatalog.Curated.First(candidate => candidate.IsEditable);
        Assert.True(session.TryAddCatalogPet(variant.ClassPath, "First", 101, 202, 303, out var message), message);
        await session.SaveAsync();

        var added = Assert.Single(WorldSaveReader.ReadFromFile(facilityPath).Pets);
        Assert.Equal(variant.ClassPath, added.NpcClass);
        Assert.Equal("First", added.CustomName);
        Assert.False(added.IsDead);
        Assert.Equal(101, added.X);
        Assert.Equal(202, added.Y);
        Assert.Equal(303, added.Z);
        if (added.LimbHealth.Count > 0) Assert.True(added.TotalHealth > 0);
    }

    [Fact]
    public async Task Sibling_bed_discovery_and_send_move_a_carried_pet_between_saves()
    {
        using var world = CopyCascadeWorld();
        var playerPath = FindPlayer(world.Path);
        var facilityPath = Path.Combine(world.Path, "WorldSave_Facility.sav");

        // The fixture players carry no pets, so seed one from the Facility pet map exactly
        // the way the editor's world PETS tab would (pick up into the hotbar, write both).
        SeedCarriedPet(playerPath, facilityPath);
        var playerBytes = File.ReadAllBytes(playerPath);
        var worldBytes = File.ReadAllBytes(facilityPath);

        // The desktop file system is the seam this service reads through; it is a pass-through
        // to System.IO, so the behaviour asserted below is the behaviour the app has.
        var service = new SiblingWorldBedService(new DesktopSaveFileSystem());

        // Discovery works without any world session loaded: the sibling scan finds the
        // region saves next to the player (never the metadata save) and their pet beds.
        var siblings = service.SiblingWorlds(playerPath, workspace: null);
        Assert.NotEmpty(siblings);
        Assert.DoesNotContain(siblings, sibling =>
            sibling.Name.Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(siblings, sibling => string.Equals(
            Path.GetFullPath(sibling.Path), Path.GetFullPath(facilityPath), StringComparison.OrdinalIgnoreCase));

        var beds = await service.GetBedsAsync(facilityPath);
        Assert.NotEmpty(beds);
        var bed = beds[0];

        // A second read-only lookup is served from the timestamp cache (same list, no rescan
        // of the multi-megabyte region save).
        Assert.Same(beds, await service.GetBedsAsync(facilityPath));

        // The send routes through a staged session: nothing touches disk yet.
        var playerSession = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(playerPath), playerPath);
        var pet = Assert.Single(playerSession.CarriedPets);
        var expectedClass = PetItemCatalog.NpcClassFor(pet.ItemRow);
        var worldSession = await service.GetOrLoadSessionAsync(facilityPath);
        Assert.True(worldSession.TryPlaceCarriedPet(pet.ToCarriedPet(), bed.X, bed.Y, bed.Z, out var message), message);
        Assert.True(worldSession.IsDirty);
        Assert.Same(worldSession, await service.GetOrLoadSessionAsync(facilityPath));
        pet.IsDeleted = true;
        Assert.Equal(worldBytes, File.ReadAllBytes(facilityPath));
        Assert.Equal(playerBytes, File.ReadAllBytes(playerPath));

        // SAVE WORLD then SAVE (player): both writes keep a .bak and the pet actually moves.
        var petsBefore = WorldSaveReader.ReadFromFile(facilityPath).Pets.Count;
        await worldSession.SaveAsync();
        await playerSession.SaveAsync();

        Assert.True(File.Exists(facilityPath + ".bak"));
        Assert.True(File.Exists(playerPath + ".bak"));
        var worldBack = WorldSaveReader.ReadFromFile(facilityPath);
        Assert.Equal(petsBefore + 1, worldBack.Pets.Count);
        Assert.Contains(worldBack.Pets, placed => placed.NpcClass == expectedClass
            && placed.Xp == pet.Xp
            && Math.Abs(placed.X - bed.X) < 1 && Math.Abs(placed.Y - bed.Y) < 1);
        Assert.Empty(PlayerSaveReader.ReadFromFile(playerPath).CarriedPets);
        Assert.Empty(playerSession.CarriedPets);

        // After the write the timestamp cache refreshes and sees the newly placed pet's world.
        var refreshed = await service.GetBedsAsync(facilityPath);
        Assert.NotEmpty(refreshed);
    }

    private static void SeedCarriedPet(string playerPath, string worldPath)
    {
        var worldData = WorldSaveReader.ReadFromFile(worldPath);
        var playerData = PlayerSaveReader.ReadFromFile(playerPath);
        Assert.NotEmpty(worldData.Pets);
        var result = PetTransfer.WorldToPlayer(worldData, worldData.Pets[0].Id, playerData, PetSlotKind.Hotbar);
        Assert.True(result.Ok, result.Message);
        WorldSaveWriter.WriteToFile(worldData, worldPath);
        PlayerSaveWriter.WriteToFile(playerData, playerPath);
        File.Delete(worldPath + ".bak");
        File.Delete(playerPath + ".bak");
    }

    private static string FindPlayer(string worldPath)
        => Directory.EnumerateFiles(Path.Combine(worldPath, "PlayerData"), "Player_*.sav")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();

    private static TempWorld CopyCascadeWorld()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var directory = Directory.CreateTempSubdirectory("web-companion-crosssave-");
        foreach (var source in Directory.EnumerateFiles(Fixtures.CascadeDir!, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(directory.FullName, Path.GetRelativePath(Fixtures.CascadeDir!, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        return new TempWorld(directory);
    }

    private sealed class TempWorld(DirectoryInfo directory) : IDisposable
    {
        public string Path => directory.FullName;
        public void Dispose()
        {
            try { directory.Delete(recursive: true); } catch (IOException) { }
        }
    }
}
