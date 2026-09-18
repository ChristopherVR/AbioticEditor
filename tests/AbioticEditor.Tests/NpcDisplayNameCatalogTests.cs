using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Round 99's <c>DT_NPCList</c>-backed name resolver behind the merged NPCS tab's "Creatures and
/// NPCs nearby" section, and the registry payload (<see cref="GameDataRegistry.NpcDisplayNames"/>)
/// that lets a browser build with no game install resolve the same names a mounted desktop
/// install would. See <c>PetGameDataTests</c> for the pets-only reader this generalizes.
/// </summary>
public class NpcDisplayNameCatalogTests
{
    // ---------- Resolve: pure, no game install needed ----------

    [Fact]
    public void Resolve_matches_a_short_class_name_case_insensitively()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NPC_Robot_Defense"] = "Defense Robot",
        };
        // A live "Label" arrives as the bare class name (see LiveNpcChannel/main.lua's
        // classLabel), always ending "_C" - the mismatch this catalog exists to fix: the class
        // name alone would naively derive "Robot Defense", not the game's own "Defense Robot".
        Assert.Equal("Defense Robot", NpcDisplayNameCatalog.Resolve(names, "npc_robot_defense_C"));
    }

    [Fact]
    public void Resolve_matches_a_full_soft_class_path()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NPC_Monster_Peccary"] = "Peccary",
        };
        // DT_NPCList's own NPCSpawnClass_ values are full soft-object paths
        // ("/Game/.../NPC_Monster_Peccary.NPC_Monster_Peccary_C") - the same shape PetCatalog.ByClass
        // already normalizes with ShortOf, reused here.
        Assert.Equal("Peccary",
            NpcDisplayNameCatalog.Resolve(names, "/Game/Blueprints/Characters/NPCs/NPC_Monster_Peccary.NPC_Monster_Peccary_C"));
    }

    [Fact]
    public void Resolve_returns_null_for_an_unknown_class_or_missing_dictionary()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Known"] = "Known Name" };
        Assert.Null(NpcDisplayNameCatalog.Resolve(names, "SomethingElse_C"));
        Assert.Null(NpcDisplayNameCatalog.Resolve(names, null));
        Assert.Null(NpcDisplayNameCatalog.Resolve(new Dictionary<string, string>(), "Known"));
        Assert.Null(NpcDisplayNameCatalog.Resolve(null, "Known"));
    }

    // ---------- LoadFrom: live table read (skips without a game install) ----------

    [Fact]
    public void Live_DT_NPCList_agrees_with_the_pets_only_reader_on_a_shared_row()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return; // no install: skip

        var names = NpcDisplayNameCatalog.LoadFrom(provider);
        Assert.NotEmpty(names);

        // Cross-checked against PetGameDataTests.Live_pet_tables_define_the_new_companions,
        // which already asserts this exact DT_NPCList row resolves "Speedogi" through the
        // pets-only reader - this general-purpose reader must agree on the same class.
        Assert.Equal("Speedogi", NpcDisplayNameCatalog.Resolve(names, "NPC_Monster_LamogiSpeedy"));
    }

    // ---------- GameDataRegistry: bundled payload round-trips ----------

    [Fact]
    public void GameDataRegistry_round_trips_NpcDisplayNames()
    {
        var registry = new GameDataRegistry
        {
            NpcDisplayNames = new Dictionary<string, string> { ["NPC_Robot_Defense"] = "Defense Robot" },
        };
        var dir = Directory.CreateTempSubdirectory("npc-display-names-registry");
        try
        {
            var path = Path.Combine(dir.FullName, "registry.json");
            registry.Save(path);
            var loaded = GameDataRegistry.TryLoad(path);
            Assert.NotNull(loaded);
            Assert.Equal("Defense Robot", loaded!.NpcDisplayNames?["NPC_Robot_Defense"]);
        }
        finally { dir.Delete(recursive: true); }
    }
}
