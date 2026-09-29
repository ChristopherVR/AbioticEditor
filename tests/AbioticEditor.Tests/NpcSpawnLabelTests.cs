using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

public class NpcSpawnLabelTests
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NPC_Monster_Pest"] = "Pest",
        ["NPC_Monster_Pest_Volatile"] = "Volatile Pest",
        ["NPC_Sapper"] = "Sapper",
    };

    [Theory]
    [InlineData("NPCSpawn_Sapper_C_4", "Sapper spawner 4")]
    [InlineData("NPCSpawn_Pest_C_7", "Pest spawner 7")]
    [InlineData("NPCSpawn_Pest_Volatile_C_9", "Volatile Pest spawner 9")]
    [InlineData("NPCSpawn_Zombie_Scientist_C_12", "Zombie Scientist spawner 12")]
    [InlineData("NPCSpawn_Sapper_C", "Sapper spawner")]
    [InlineData("SomethingElse_C_1", "SomethingElse_C_1")]
    public void Spawner_names_become_friendly_labels(string actor, string expected)
        => Assert.Equal(expected, NpcSpawnMapFeature.FriendlyLabel(actor, Names));

    [Fact]
    public void Without_game_names_the_creature_is_still_readable()
        => Assert.Equal("Pest Volatile spawner 2", NpcSpawnMapFeature.FriendlyLabel("NPCSpawn_Pest_Volatile_C_2", null));
}
