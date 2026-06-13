using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>Pets (PetNPC map): read, rename, and round-trip through the writer.</summary>
public class PetEditTests
{
    [Fact]
    public void Fixture_pets_are_read_with_names_and_classes()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var facility = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var pets = WorldSaveReader.ReadFromFile(facility).Npcs.Where(n => n.IsPet).ToList();

        Assert.True(pets.Count >= 10, $"fixture has 12 pets, got {pets.Count}");
        Assert.Contains(pets, p => p.CustomName == "Rex");
        Assert.All(pets, p => Assert.False(string.IsNullOrEmpty(p.NpcClass)));
    }

    [Fact]
    public void Renaming_a_pet_round_trips()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var dir = Directory.CreateTempSubdirectory("pet-rename");
        try
        {
            var copy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(source, copy);

            var data = WorldSaveReader.ReadFromFile(copy);
            var rex = data.Npcs.First(n => n.IsPet && n.CustomName == "Rex");
            var unnamed = data.Npcs.First(n => n.IsPet && string.IsNullOrEmpty(n.CustomName));

            var updated = data.Npcs
                .Select(n => n.Id == rex.Id ? n with { CustomName = "Sparky" }
                    : n.Id == unnamed.Id ? n with { CustomName = "Maja" }
                    : n)
                .ToList();
            WorldSaveWriter.ApplyNpcs(data, updated);
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Npcs.Where(n => n.IsPet).ToList();
            Assert.Contains(reloaded, p => p.Id == rex.Id && p.CustomName == "Sparky");
            Assert.Contains(reloaded, p => p.Id == unnamed.Id && p.CustomName == "Maja");
            Assert.DoesNotContain(reloaded, p => p.CustomName == "Rex");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
