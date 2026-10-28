using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// The browser build only knows the base game, so a save played with mods holds ids it cannot name.
/// These tests pin that such a save is reported honestly and still edits safely.
/// </summary>
public sealed class ModdedContentTests
{
    private const string ModdedItem = "NR69O_syringe_test";
    private const string ModdedRecipe = "NR69O_recipe_test";
    private const string ModdedLine = "NR69O_QuestLine_Test";

    private static PlayerSaveData Player(string fixture = "PlayerData/Player_76561197993781479.sav")
        => PlayerSaveReader.ReadFromFile(CommunityUpdateFiveTests.Fixture(fixture));

    private static ModdedContentVocabulary BaseGame()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "assets", "registry", "registry.json");
            if (File.Exists(path))
                return ModdedContentVocabulary.FromRegistry(GameDataRegistry.TryLoad(path) ?? throw new InvalidDataException(path));
        }
        throw new FileNotFoundException("assets/registry/registry.json");
    }

    private static PlayerSaveData RoundTrip(PlayerSaveData data)
    {
        using var stream = new MemoryStream();
        data.Raw.WriteTo(stream);
        stream.Position = 0;
        return PlayerSaveReader.ReadFromStream(stream);
    }

    [Fact]
    public void Game_written_base_saves_report_nothing_so_the_banner_never_cries_wolf()
    {
        var vocabulary = BaseGame();
        Assert.False(ModdedContentScanner.ScanPlayer(Player(), vocabulary).HasAny);
        Assert.False(ModdedContentScanner.ScanPlayer(Player("Validation/Player_76561197993781479.sav"), vocabulary).HasAny);

        var world = WorldSaveReader.ReadFromFile(CommunityUpdateFiveTests.Fixture("WorldSave_Facility.sav"));
        Assert.False(ModdedContentScanner.ScanWorld(world.Containers, world.DroppedItems, world.GlobalRecipes, vocabulary).HasAny);
        var meta = WorldSaveReader.ReadFromFile(CommunityUpdateFiveTests.Fixture("WorldSave_MetaData.sav"));
        Assert.False(ModdedContentScanner.ScanWorld(meta.Containers, meta.DroppedItems, meta.GlobalRecipes, vocabulary).HasAny);
    }

    [Fact]
    public void Modded_ids_in_a_player_save_are_found_by_kind()
    {
        var data = Player();
        var slots = data.Inventory.Main.ToArray();
        slots[0] = slots[0] with { ItemId = ModdedItem, Count = 1 };
        PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = slots });
        PlayerSaveWriter.ApplyRecipes(data, data.Recipes.Append(ModdedRecipe).ToArray());
        PlayerQuestSave.TrackLine(data, ModdedLine, new QuestCatalogData([], [new(ModdedLine, "Test", false, null, [])]));

        var report = ModdedContentScanner.ScanPlayer(RoundTrip(data), BaseGame());

        Assert.Equal([ModdedItem], report.Items);
        Assert.Equal([ModdedRecipe], report.Recipes);
        Assert.Equal([ModdedLine], report.QuestLines);
        Assert.Equal(3, report.Total);
    }

    [Fact]
    public void Modded_items_in_world_containers_and_on_the_ground_are_found()
    {
        var modded = new InventoryItemSlot(0, ModdedItem, 1, 0, 0, 0, 0, null, false, null, null);
        var empty = new InventoryItemSlot(1, "None", 0, 0, 0, 0, 0, null, false, null, null);
        var container = new WorldContainer("a", WorldContainerSource.Custom, null, [new WorldInventory([modded, empty])]);
        var dropped = new WorldDroppedItem("b", modded with { ItemId = ModdedItem.ToUpperInvariant() }, false);

        var report = ModdedContentScanner.ScanWorld([container], [dropped], [ModdedRecipe], BaseGame());

        Assert.Single(report.Items);
        Assert.Equal([ModdedRecipe], report.Recipes);
    }

    [Fact]
    public void A_host_with_no_game_data_judges_nothing()
    {
        var data = Player();
        var slots = data.Inventory.Main.ToArray();
        slots[0] = slots[0] with { ItemId = ModdedItem, Count = 1 };
        PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = slots });

        var none = new ModdedContentVocabulary(null, null, null, null, null);
        Assert.False(ModdedContentScanner.ScanPlayer(RoundTrip(data), none).HasAny);
        Assert.False(ModdedContentScanner.ScanPlayer(data, ModdedContentVocabulary.FromRegistry(new GameDataRegistry())).HasAny);
    }

    [Fact]
    public async Task An_unknown_tracked_quest_line_survives_unrelated_edits_and_cannot_be_chosen_blindly()
    {
        var directory = Directory.CreateTempSubdirectory("abiotic-modded-quest-");
        try
        {
            var modded = Player();
            PlayerQuestSave.TrackLine(modded, ModdedLine, new QuestCatalogData([], [new(ModdedLine, "Test", false, null, [])]));
            var path = Path.Combine(directory.FullName, "Player_76561197993781479.sav");
            using (var file = File.Create(path)) modded.Raw.WriteTo(file);

            var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
            Assert.Equal(ModdedLine, session.TrackedQuestLine);

            var baseCatalog = new QuestCatalogData([], [new("QuestLine_Kylie", "Kylie", false, null, [])]);
            session.TrackQuestLine("NR69O_not_a_real_line", baseCatalog);
            Assert.Equal(ModdedLine, session.TrackedQuestLine);
            Assert.False(session.IsDirty);

            session.Vitals.Money += 7;
            await session.SaveAsync();

            var saved = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal(ModdedLine, PlayerQuestSave.Read(saved).TrackedLine);
            Assert.Equal(modded.Stats.Money + 7, saved.Stats.Money);

            session.TrackQuestLine("QuestLine_Kylie", baseCatalog);
            session.TrackQuestLine(null, baseCatalog);
            await session.SaveAsync();
            Assert.Null(PlayerQuestSave.Read(PlayerSaveReader.ReadFromFile(path)).TrackedLine);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void An_unrecognised_pet_mutation_is_kept_through_other_pet_edits_and_reported()
    {
        var vocabulary = BaseGame();
        Assert.Contains(17, vocabulary.NativeMutations!["Peccary"]);

        const int modMutation = 4242;
        var data = Player("Validation/Player_76561197993781479.sav");
        var pet = Assert.Single(data.CarriedPets);
        Assert.False(ModdedContentScanner.ScanPlayer(data, vocabulary).HasAny);

        PlayerSaveWriter.ApplyCarriedPet(data, pet with { PetMutation = modMutation });
        var afterMutation = RoundTrip(data);
        Assert.Equal(modMutation, Assert.Single(afterMutation.CarriedPets).PetMutation);
        Assert.Equal(1, ModdedContentScanner.ScanPlayer(afterMutation, vocabulary).PetMutations);

        PlayerSaveWriter.ApplyCarriedPet(afterMutation, afterMutation.CarriedPets[0] with { Xp = pet.Xp + 100 });
        var edited = Assert.Single(RoundTrip(afterMutation).CarriedPets);
        Assert.Equal(modMutation, edited.PetMutation);
        Assert.Equal(pet.Xp + 100, edited.Xp);
    }
}
