using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>Pets (PetNPC map): read, edit, delete, and round-trip through the writer.</summary>
public class PetEditTests
{
    private static string CopyFacility(out string dir)
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var source = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");
        var temp = Directory.CreateTempSubdirectory("pet-edit");
        dir = temp.FullName;
        var copy = Path.Combine(dir, "WorldSave_Facility.sav");
        File.Copy(source, copy);
        return copy;
    }

    [Fact]
    public void Fixture_pets_are_read_with_names_classes_health_and_xp()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var facility = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");

        var pets = WorldSaveReader.ReadFromFile(facility).Pets;

        Assert.True(pets.Count >= 10, $"fixture has 12 pets, got {pets.Count}");
        Assert.Contains(pets, p => p.CustomName == "Rex");
        Assert.All(pets, p => Assert.False(string.IsNullOrEmpty(p.NpcClass)));
        // Every pet carries the per-limb health map and a (>=0) XP value.
        Assert.All(pets, p => Assert.NotEmpty(p.LimbHealth));
        Assert.All(pets, p => Assert.True(p.Xp >= 0));
        // Rex is an Electro Pest in the Pest family.
        var rex = pets.First(p => p.CustomName == "Rex");
        Assert.Equal(PetCategory.Pest, PetCatalog.Categorize(rex.NpcClass));
    }

    [Fact]
    public void Editing_name_health_xp_and_class_round_trips()
    {
        var copy = CopyFacility(out var dir);
        try
        {
            var data = WorldSaveReader.ReadFromFile(copy);
            var rex = data.Pets.First(p => p.CustomName == "Rex");

            // Pick one limb to change, keep the rest.
            var limbKey = rex.LimbHealth.Keys.First();
            var newHealth = new Dictionary<string, double>(rex.LimbHealth) { [limbKey] = 42 };
            const string magma = "/Game/Blueprints/Characters/NPCs/NPC_Monster_Pest_Magma.NPC_Monster_Pest_Magma_C";

            var updated = data.Pets
                .Select(p => p.Id == rex.Id
                    ? p with { CustomName = "Sparky", IsDead = true, LimbHealth = newHealth, Xp = 300, NpcClass = magma }
                    : p)
                .ToList();
            WorldSaveWriter.ApplyPets(data, updated);
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Pets.First(p => p.Id == rex.Id);
            Assert.Equal("Sparky", reloaded.CustomName);
            Assert.True(reloaded.IsDead);
            Assert.Equal(42, reloaded.LimbHealth[limbKey]);
            Assert.Equal(300, reloaded.Xp);
            Assert.Equal("NPC_Monster_Pest_Magma", reloaded.ShortClass);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Deleting_a_pet_removes_only_that_entry()
    {
        var copy = CopyFacility(out var dir);
        try
        {
            var data = WorldSaveReader.ReadFromFile(copy);
            var before = data.Pets.Count;
            var rex = data.Pets.First(p => p.CustomName == "Rex");
            var survivor = data.Pets.First(p => p.Id != rex.Id);

            Assert.True(WorldSaveWriter.RemovePet(data, rex.Id));
            WorldSaveWriter.WriteToFile(data, copy);

            var reloaded = WorldSaveReader.ReadFromFile(copy).Pets;
            Assert.Equal(before - 1, reloaded.Count);
            Assert.DoesNotContain(reloaded, p => p.Id == rex.Id);
            Assert.Contains(reloaded, p => p.Id == survivor.Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Pets_are_no_longer_mixed_into_the_npc_list()
    {
        Assert.NotNull(Fixtures.ServerWorldsDir);
        var facility = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_Facility.sav");
        var data = WorldSaveReader.ReadFromFile(facility);

        Assert.NotEmpty(data.Pets);
        Assert.All(data.Npcs, n => Assert.False(n.IsPet));
    }
}
