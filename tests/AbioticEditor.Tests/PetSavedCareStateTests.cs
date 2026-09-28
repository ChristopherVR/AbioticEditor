using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Read-only census of what a saved pet actually stores. The save has no food identity, no
/// cooldown field by name and no "last fed" stamp: only these dynamic ints.
/// </summary>
public sealed class PetSavedCareStateTests
{
    private static readonly string[] KnownKeys =
        ["Portions", "TimerState", "XP", "CurrentAmmo", "Generic2", "MutationProgress", "PetMutation"];

    [Fact]
    public void World_pets_store_only_xp_and_opaque_timer_ints()
    {
        var dir = Fixtures.ServerWorldsDir;
        if (dir is null) return;
        var path = Path.Combine(dir, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var pets = PetSavedCareState.ReadWorld(WorldSaveReader.ReadFromFile(path).Raw);
        Assert.Equal(12, pets.Count);
        Assert.All(pets, p =>
        {
            Assert.NotNull(p.Xp);
            Assert.True(p.TimerState > 0);
            Assert.All(p.Dynamic.Keys, k => Assert.Contains(k, KnownKeys));
            Assert.Null(p.PetMutation);
            Assert.Null(p.MutationProgress);
        });
        var sows = pets.Where(p => p.Species == "NPC_Peccary_Sow").ToList();
        Assert.Equal(10, sows.Count);
        Assert.All(sows, p => Assert.True(p.Generic2 > p.TimerState));
        Assert.Contains(pets, p => p.Species == "NPC_Monster_Pest_Electro" && p.Xp == 50 && p.Portions == 1);
        Assert.Contains(pets, p => p.Species == "NPC_Skink_Crafted" && p.Dynamic["CurrentAmmo"] == 168 && p.Generic2 is null);
    }

    [Fact]
    public void Carried_pets_add_mutation_progress_and_target()
    {
        var dir = Fixtures.ServerWorldsDir;
        if (dir is null) return;
        var found = new Dictionary<string, PetSavedCareState>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir, "PlayerData"), "Player_*.sav"))
            foreach (var pet in PetSavedCareState.ReadCarried(PlayerSaveReader.ReadFromFile(file).Raw))
                found[pet.Species ?? ""] = pet;
        var leyak = found["Pest_Leyak"];
        Assert.Equal(6, leyak.PetMutation);
        Assert.Equal(3, leyak.MutationProgress);
        Assert.Equal(436894, leyak.TimerState);
        Assert.Equal("EquipmentInventory[12]", leyak.Id);
        var skink = found["Skink_Magma_Crafted"];
        Assert.Equal(1, skink.PetMutation);
        Assert.Equal(448843, skink.TimerState);
        Assert.All(found.Values, p => Assert.All(p.Dynamic.Keys, k => Assert.Contains(k, KnownKeys)));
    }
}
