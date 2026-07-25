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

        var service = new SiblingWorldBedService();

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
