using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Newtonsoft.Json;
using AbioticEditor.Core.Saves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>Opt-in preparation and snapshots of an isolated game validation profile.</summary>
public sealed class NativeSaveGameValidationProbe
{
    [Fact]
    public void Prepare_or_inspect_isolated_profile()
    {
        var profile = Environment.GetEnvironmentVariable("ABIOTIC_VALIDATION_PROFILE");
        if (string.IsNullOrEmpty(profile)) return;
        Assert.True(File.Exists(Path.Combine(profile, ".editor-validation-profile")));
        var worldPath = Directory.GetFiles(profile, "WorldSave_Facility.sav", SearchOption.AllDirectories).Single();
        var playerPath = Directory.GetFiles(profile, "Player_*.sav", SearchOption.AllDirectories).Single();
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        Assert.NotNull(provider);
        PetCatalog.ApplyGameData(PetGameData.TryLoadFrom(provider));
        var player = PlayerSaveReader.ReadFromFile(playerPath);
        var mode = Environment.GetEnvironmentVariable("ABIOTIC_VALIDATION_MODE") ?? "inspect";
        if (mode == "complete-bed")
        {
            var setup = WorldSaveReader.ReadFromFile(worldPath);
            var map = Assert.IsType<MapProperty>(setup.Raw.Properties.FindByPrefix("DeployedObjectMap")!.Property);
            var bed = Assert.Single(map.Value!, pair => pair.Value is StructProperty { Value: PropertiesStruct p }
                && p.Properties.FindByPrefix("DeployableClass")?.Property?.Value?.ToString()?.Contains("Deployed_PetBed_Medium", StringComparison.Ordinal) == true);
            var fields = Assert.IsType<PropertiesStruct>(Assert.IsType<StructProperty>(bed.Value).Value).Properties;
            fields.FindByPrefix("bConstructionMode")!.Property!.Value = false;
            fields.FindByPrefix("ConstructionLevel")!.Property!.Value = 100d;
            WorldSaveWriter.WriteToFile(setup, worldPath);
            PlayerSaveWriter.ApplyRespawn(player, -19030.9249, 12899.4125, 208.15);
            PlayerQuestSave.TrackLine(player, "QuestLine_Grayson", QuestCatalog.LoadFrom(provider)!);
            PlayerSaveWriter.WriteToFile(player, playerPath);
            player = PlayerSaveReader.ReadFromFile(playerPath);
        }
        if (mode == "bed")
        {
            var slots = player.Inventory.Hotbar.ToArray();
            Assert.True(slots[0].ItemId is "None" or "Empty" or "");
            slots[0] = slots[0] with { ItemId = "petbed", Count = 2, Durability = 100, MaxDurability = 100 };
            PlayerSaveWriter.ApplyInventory(player, player.Inventory with { Hotbar = slots });
            PlayerSaveWriter.WriteToFile(player, playerPath);
            player = PlayerSaveReader.ReadFromFile(playerPath);
        }
        if (mode == "prepare")
        {
            Assert.Empty(player.CarriedPets);
            PlayerSaveWriter.ApplyStats(player, player.Stats with { Money = 12345 });
            PlayerSaveWriter.ApplyRecipes(player, player.Recipes.Concat(["recipe_bandage"]).Distinct().ToArray());
            PlayerSaveWriter.ApplySkills(player, player.Skills.Select(s => s with { Xp = 1200 }).ToArray());
            PlayerQuestSave.TrackLine(player, "QuestLine_Kylie", QuestCatalog.LoadFrom(provider)!);
            Assert.True(PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Hotbar, -1,
                new(PetSlotKind.Hotbar, -1, "Peccary_Fog", "Fogbound", 100, 100, 1800, 0, 0)) >= 0);
            Assert.True(PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Hotbar, -1,
                new(PetSlotKind.Hotbar, -1, "pest", "TransferTest", 100, 100, 900, 0, 0)) >= 0);
            PlayerSaveWriter.WriteToFile(player, playerPath);
            player = PlayerSaveReader.ReadFromFile(playerPath);
        }
        var world = WorldSaveReader.ReadFromFile(worldPath);
        var output = Path.Combine(profile, "validation-" + mode + ".json");
        File.WriteAllText(output, JsonConvert.SerializeObject(new
        {
            player.Stats, player.Skills, player.Recipes, player.Inventory,
            Pets = player.CarriedPets, Quests = PlayerQuestSave.Read(player), WorldPets = world.Pets,
        }, Formatting.Indented));
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(worldPath)!, "*.sav", SearchOption.AllDirectories))
        {
            using var raw = File.OpenRead(path);
            using var json = File.Create(Path.Combine(profile, Path.GetFileNameWithoutExtension(path) + "-" + mode + ".json"));
            new UeSaveGame.Json.SaveGameSerializer().ConvertToJson(raw, json);
        }
    }
}
