using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

public sealed class CommunityUpdateFiveTests
{
    internal static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "fixtures", "Update1_5", name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(name);
    }
    private static PlayerSaveData Player() => PlayerSaveReader.ReadFromFile(Fixture("PlayerData/Player_76561197993781479.sav"));

    [Fact]
    public void Game_written_validation_save_retains_edited_progress_and_fog_companion()
    {
        var path = Fixture("Validation/Player_76561197993781479.sav");
        var player = PlayerSaveReader.ReadFromFile(path);
        Assert.Equal(12345, player.Stats.Money);
        Assert.All(player.Skills, skill => Assert.Equal(1200, skill.Xp));
        Assert.Contains("recipe_bandage", player.Recipes);
        Assert.Equal("QuestLine_Kylie", PlayerQuestSave.Read(player).TrackedLine);
        var pet = Assert.Single(player.CarriedPets);
        Assert.Equal("Peccary_Fog", pet.ItemRow);
        Assert.Equal("Fogbound", pet.Name);
        Assert.Equal(PetSlotKind.Equipment, pet.Slot);
        Assert.Equal(12, pet.Index);
        Assert.Equal(1800, pet.Xp);
        Assert.Equal(3580, pet.Health);
        Assert.Equal(0, pet.PetMutation);
        Assert.Equal(0, pet.MutationProgress);
        Assert.Equal("Peccary_Fog", PetItemCatalog.ItemRowFor(PetItemCatalog.NpcClassFor("Peccary_Fog")));
        using var bytes = new MemoryStream();
        player.Raw.WriteTo(bytes);
        Assert.Equal(File.ReadAllBytes(path), bytes.ToArray());
    }

    [Fact]
    public async Task Tracking_stages_reverts_and_saves_with_the_original_backup()
    {
        var directory = Directory.CreateTempSubdirectory("abiotic-native-quest-");
        try
        {
            var path = Path.Combine(directory.FullName, "Player_76561197993781479.sav");
            File.Copy(Fixture("PlayerData/Player_76561197993781479.sav"), path);
            var original = File.ReadAllBytes(path);
            var session = new AbioticEditor.Web.Models.PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
            var catalog = new QuestCatalogData([], [new("QuestLine_Kylie", "Kylie", false, null, [])]);
            var initial = session.TrackedQuestLine;
            session.TrackQuestLine("QuestLine_Kylie", catalog);
            Assert.True(session.IsDirty);
            Assert.Equal(original, File.ReadAllBytes(path));
            session.Revert();
            Assert.Equal(initial, session.TrackedQuestLine);
            Assert.False(session.IsDirty);
            session.TrackQuestLine("QuestLine_Kylie", catalog);
            await session.SaveAsync();
            Assert.Equal("QuestLine_Kylie", PlayerQuestSave.Read(PlayerSaveReader.ReadFromFile(path)).TrackedLine);
            Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
            Assert.False(session.IsDirty);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Installed_native_catalog_keeps_new_pet_targets_and_lazy_world_exports()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is not { HasMappings: true }) return;
        var world = provider.LoadPackageInternal("AbioticFactor/Content/Maps/Facility_Office1");
        Assert.DoesNotContain(world.ExportsLazy, export => export.IsValueCreated);
        var spawns = NpcSpawnCatalog.LoadFrom(provider);
        Assert.Equal("NPC_Gatekeeper_Chieftain", spawns["NPCSpawn_Gatekeeper_Chieftain_C"]);
        Assert.Equal("NPC_Gatekeeper_Grunt", spawns["NPCSpawn_Gatekeeper_Phyter_C"]);
        var options = PetCareCatalog.MutationOptionsFor(provider, "Peccary");
        var fog = Assert.Single(options, option => option.TargetRow == "Peccary_Fog");
        Assert.Equal(17, fog.Value);
        Assert.Contains("filet_fogfish_raw", fog.Foods);
        Assert.Contains("tincture_phasing", fog.Foods);
        Assert.Contains("food_nigiri_fog_slice", fog.Foods);
    }

    [Fact]
    public void Installed_native_catalogs_read_trader_stock_trait_buffs_and_npc_states()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is not { HasMappings: true }) return;
        var blacksmith = Assert.Single(AbioticEditor.Core.Codex.TraderCatalog.LoadFrom(provider), t => t.Id == "Blacksmith");
        Assert.NotEmpty(blacksmith.Sells);
        Assert.All(blacksmith.Sells, offer => Assert.False(string.IsNullOrEmpty(offer.ItemId)));
        Assert.Contains(TraitCatalog.LoadDetailsFrom(provider).Values, t => !string.IsNullOrEmpty(t.BuffRowName));
        Assert.Contains("FirstContactMade", NpcStateCatalog.LoadFrom(provider));
    }

    [Fact]
    public void First_native_pet_keeps_xp_and_mutation_without_a_legacy_template()
    {
        var data = Player();
        var pet = new CarriedPet(PetSlotKind.Hotbar, -1, "Peccary", "Fogbound", 100, 100, 1800, 2, 17);
        var index = PlayerSaveWriter.AddCarriedPetToSlot(data, PetSlotKind.Hotbar, -1, pet);
        Assert.True(index >= 0);
        using var stream = new MemoryStream(); data.Raw.WriteTo(stream); stream.Position = 0;
        var read = PlayerSaveReader.ReadFromStream(stream);
        var saved = Assert.Single(read.CarriedPets, p => p.ItemRow == "Peccary");
        Assert.Equal(1800, saved.Xp);
        Assert.Equal(2, saved.MutationProgress);
        Assert.Equal(17, saved.PetMutation);
    }

    [Fact]
    public void Native_inventory_edits_keep_typed_handles_and_create_native_appearance()
    {
        var data = Player();
        var slots = data.Inventory.Main.ToArray();
        slots[0] = slots[0] with { ItemId = "Peccary_Fog", Count = 1, Durability = 100, MaxDurability = 100, VariantRowName = "variant_test" };
        PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = slots });
        using var stream = new MemoryStream(); data.Raw.WriteTo(stream); stream.Position = 0;
        var read = PlayerSaveReader.ReadFromStream(stream);
        Assert.Equal("Peccary_Fog", read.Inventory.Main[0].ItemId);
        Assert.Equal("variant_test", read.Inventory.Main[0].VariantRowName);
        var root = PlayerSaveReader.GetCharacterSaveData(read.Raw);
        var inventory = Assert.IsType<ArrayProperty>(root.FindByPrefix("Inventory_")!.Property);
        var slot = Assert.IsType<PropertiesStruct>(Assert.IsType<StructProperty>(inventory.Value!.GetValue(0)).Value);
        var item = Assert.IsType<StructProperty>(slot.Properties.FindByPrefix("ItemDataTable_")!.Property);
        Assert.Equal("GlobalItemRowHandle", item.StructType!.Name.Value);
        Assert.Null(Assert.IsType<PropertiesStruct>(item.Value).Properties.FindByPrefix("DataTable"));
        var changeable = Assert.IsType<PropertiesStruct>(Assert.IsType<StructProperty>(slot.Properties.FindByPrefix("ChangeableData_")!.Property).Value);
        Assert.DoesNotContain(changeable.Properties, tag => tag.Name?.Value?.Contains('_') == true);
        var variant = Assert.IsType<StructProperty>(changeable.Properties.FindByPrefix("TextureVariantRow_")!.Property);
        Assert.Equal("TextureVariantRowHandle", variant.StructType!.Name.Value);
    }

    [Theory]
    [InlineData("WorldSave_MetaData.sav")]
    [InlineData("WorldSave_Facility.sav")]
    public void Native_world_read_is_byte_exact(string name)
    {
        var data = WorldSaveReader.ReadFromFile(Fixture(name));
        using var stream = new MemoryStream(); data.Raw.WriteTo(stream);
        Assert.Equal(File.ReadAllBytes(Fixture(name)), stream.ToArray());
    }

    [Fact]
    public void Native_player_reads_real_stats_recipes_skills_and_inventory()
    {
        var data = Player();
        Assert.Equal(106.39999985694885, data.Stats.Hunger, 6);
        Assert.Equal(3.1679999828338623, data.Stats.Fatigue, 6);
        Assert.Equal(940, data.Skills[0].Xp);
        Assert.Equal(15, data.Skills.Count);
        Assert.Contains("recipe_bench_crafting", data.Recipes);
        Assert.True(data.CompletedIntro);
        Assert.Equal(13, data.Inventory.Equipment.Count);
        Assert.DoesNotContain("Properties", data.Phd ?? string.Empty);
        using var output = new MemoryStream();
        data.Raw.WriteTo(output);
        Assert.Equal(File.ReadAllBytes(Fixture("PlayerData/Player_76561197993781479.sav")), output.ToArray());
    }

    [Fact]
    public void Native_edits_create_exact_names_and_nested_codex_without_touching_other_fields()
    {
        var data = Player();
        PlayerSaveWriter.ApplyTraits(data, ["Trait_LeadBelly"]);
        PlayerSaveWriter.ApplyStats(data, data.Stats with { Money = 123 });
        PlayerSaveWriter.ApplyEmailsRead(data, ["email_test"]);
        PlayerSaveWriter.ApplyJournals(data, ["journal_test"]);
        PlayerSaveWriter.ApplyPhd(data, "PhD_HumanBio");
        using var stream = new MemoryStream();
        data.Raw.WriteTo(stream);
        stream.Position = 0;
        var read = PlayerSaveReader.ReadFromStream(stream);
        Assert.Equal(123, read.Stats.Money);
        Assert.Equal(["Trait_LeadBelly"], read.Traits);
        Assert.Equal(["email_test"], read.EmailsRead);
        Assert.Equal(["journal_test"], read.Journals);
        Assert.Equal(["journal_test"], read.JournalUnread);
        Assert.Equal("PhD_HumanBio", read.Phd);
        var root = PlayerSaveReader.GetCharacterSaveData(read.Raw);
        Assert.Contains(root, tag => tag.Name?.Value == "CurrentMoney");
        Assert.DoesNotContain(root, tag => tag.Name?.Value?.Contains('_') == true);
        Assert.DoesNotContain(root, tag => tag.Name?.Value == "EmailsRead");
    }

    [Fact]
    public async Task Native_traits_are_trait_row_handles_and_a_character_with_traits_still_saves()
    {
        var directory = Directory.CreateTempSubdirectory("abiotic-native-traits-");
        try
        {
            // The game writes each native trait as an AnyTraitRowHandle struct, not a bare name.
            var data = Player();
            PlayerSaveWriter.ApplyTraits(data, ["Trait_Dry", "Trait_LeadBelly"]);
            var traits = PlayerSaveReader.GetCharacterSaveData(data.Raw).FindByPrefix("Traits_")!;
            Assert.Equal("Traits", traits.Name.Value);
            var array = Assert.IsType<ArrayProperty>(traits.Property);
            Assert.Equal("StructProperty", array.ItemType!.Name.Value);
            Assert.Equal("AnyTraitRowHandle", array.ItemType.Parameters.Single().Name.Value);
            Assert.Equal("/Script/AbioticFactor", array.ItemType.Parameters.Single().Parameters.Single().Name.Value);
            var path = Path.Combine(directory.FullName, "Player_76561197993781479.sav");
            using (var file = File.Create(path)) data.Raw.WriteTo(file);

            // A character that already has traits must save on every edit, not only trait edits.
            var reread = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal(["Trait_Dry", "Trait_LeadBelly"], reread.Traits);
            using var before = new MemoryStream();
            reread.Raw.WriteTo(before);
            PlayerSaveWriter.ApplyTraits(reread, reread.Traits);
            using var unchanged = new MemoryStream();
            reread.Raw.WriteTo(unchanged);
            Assert.Equal(before.ToArray(), unchanged.ToArray());

            var session = new AbioticEditor.Web.Models.PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
            session.Traits.Remove("Trait_Dry");
            session.Traits.Add("Trait_Hearty");
            await session.SaveAsync();
            Assert.Equal(["Trait_LeadBelly", "Trait_Hearty"], PlayerSaveReader.ReadFromFile(path).Traits);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Multiple_quests_can_be_active_and_completion_wins_over_start()
    {
        var first = new QuestDefinition("a", "A", "", "start", "end", 0, [], null);
        var second = first with { Id = "b", StartFlag = "other" };
        var flags = new HashSet<string>(["start", "other"]);
        Assert.Equal(QuestState.Active, QuestCatalog.StateOf(first, flags));
        Assert.Equal(QuestState.Active, QuestCatalog.StateOf(second, flags));
        Assert.Equal(QuestState.Unknown, QuestCatalog.StateOf(first, null));
        Assert.Equal(QuestState.Unknown, QuestCatalog.StateOf(first with { StartFlag = null, CompletionFlag = null }, flags));
        Assert.Equal(QuestState.Completed, QuestCatalog.StateOf(first, new HashSet<string>(["end"])));
    }

    [Fact]
    public void Tracking_writes_only_the_native_personal_selection_and_preserves_server_quest()
    {
        var data = Player();
        var before = PlayerQuestSave.Read(data);
        var catalog = new QuestCatalogData([], [new("QuestLine_Kylie", "Kylie", false, null, [])]);
        PlayerQuestSave.TrackLine(data, "QuestLine_Kylie", catalog);
        using var output = new MemoryStream();
        data.Raw.WriteTo(output); output.Position = 0;
        var after = PlayerQuestSave.Read(PlayerSaveReader.ReadFromStream(output));
        Assert.True(after.SupportsMultipleQuests);
        Assert.Equal("QuestLine_Kylie", after.TrackedLine);
        Assert.Equal(before.ServerQuest, after.ServerQuest);
        Assert.Throws<ArgumentException>(() => PlayerQuestSave.TrackLine(data, "missing", catalog));
        PlayerQuestSave.TrackLine(data, null, catalog);
        Assert.Null(PlayerQuestSave.Read(data).TrackedLine);
    }

    [Fact]
    public void Metadata_recipe_sets_use_native_struct_and_set_types()
    {
        var data = WorldSaveReader.ReadFromFile(Fixture("WorldSave_MetaData.sav"));
        WorldSaveWriter.ApplyGlobalRecipes(data, ["recipe_bandage"]);
        using var output = new MemoryStream(); data.Raw.WriteTo(output); output.Position = 0;
        var after = WorldSaveReader.ReadFromStream(output);
        Assert.Contains("recipe_bandage", after.GlobalRecipes);
        var props = WorldSaveReader.GetGlobalUnlocksProps(after.Raw)!;
        Assert.IsType<SetProperty>(props.FindByPrefix("GlobalRecipesUnlocked_")!.Property);
    }
}
