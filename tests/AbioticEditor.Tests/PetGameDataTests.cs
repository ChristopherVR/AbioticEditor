using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// The DT_Pets-driven companion data (anniversary update): live table loading, the
/// item&lt;-&gt;creature bridge, and the carried-pet reader's unknown-row behavior.
/// These tests never call PetCatalog.ApplyGameData - the process-wide overlay would race
/// with other test classes running in parallel; everything asserts on pure results.
/// </summary>
public class PetGameDataTests
{
    // ---------- curated fallback covers the v1.4.0 companions ----------

    [Theory]
    [InlineData("WinterSprite", "Lamogi")]
    [InlineData("Lamogi_Plated", "Sir Ogi")]
    [InlineData("Lamogi_Speedy", "Speedogi")]
    [InlineData("Skink_Mushroom", "Verdant Skink")]
    [InlineData("Skink_Mushroom_Crafted", "Verdant Skink")]
    public void New_pet_item_rows_are_recognized(string itemRow, string friendly)
    {
        Assert.True(PetItemCatalog.IsPetItem(itemRow));
        Assert.Equal(friendly, PetItemCatalog.FriendlyName(itemRow));
    }

    [Fact]
    public void New_pet_items_bridge_to_their_creature_classes_and_back()
    {
        var speedogi = PetItemCatalog.NpcClassFor("Lamogi_Speedy");
        Assert.NotNull(speedogi);
        Assert.Contains("LamogiSpeedy", speedogi);
        Assert.Equal("Lamogi_Speedy", PetItemCatalog.ItemRowFor(speedogi));

        Assert.Equal("Lamogi_Plated", PetItemCatalog.ItemRowFor("NPC_Monster_LamogiPlated"));
        Assert.Equal("WinterSprite", PetItemCatalog.ItemRowFor("NPC_Monster_WinterSprite"));

        // Verdant Skink: held item maps to the held creature, weapon item to the crafted one.
        Assert.Contains("Skink_Mushroom.", PetItemCatalog.NpcClassFor("Skink_Mushroom"));
        Assert.Contains("Skink_Mushroom_Crafted", PetItemCatalog.NpcClassFor("Skink_Mushroom_Crafted"));
    }

    // ---------- live table loading (skips without a game install) ----------

    [Fact]
    public void Live_pet_tables_define_the_new_companions()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return; // no install: skip

        var data = PetGameData.TryLoadFrom(provider);
        Assert.NotNull(data);

        // The game's own list: every definition rooted in a family, classes resolved.
        Assert.True(data!.Definitions.Count >= 27, $"expected >=27 pets, got {data.Definitions.Count}");

        var speedogi = data.ByPetRow("Lamogi_Speedy");
        Assert.NotNull(speedogi);
        Assert.Equal("Speedogi", speedogi!.DisplayName);
        Assert.Equal("WinterSprite", speedogi.FamilyRow);
        Assert.Equal(PetCategory.Lamogi, speedogi.Category);
        Assert.Contains("NPC_Monster_LamogiSpeedy", speedogi.ClassPath);
        Assert.Equal("Lamogi_Speedy", speedogi.ItemRow);

        var sirOgi = data.ByItemRow("Lamogi_Plated");
        Assert.NotNull(sirOgi);
        Assert.Equal("Sir Ogi", sirOgi!.DisplayName);

        var verdant = data.ByClass("NPC_Skink_Mushroom");
        Assert.NotNull(verdant);
        Assert.Equal("Verdant Skink", verdant!.DisplayName);
        Assert.Equal(PetCategory.Skink, verdant.Category);
        Assert.False(verdant.IsWeaponForm);

        // The legacy base-skink pair bridges by display name.
        Assert.Equal("pet_skink", data.ByPetRow("Skink")!.ItemRow);
        Assert.Equal("biocannon", data.ByPetRow("Skink_Crafted")!.ItemRow);

        // The base Lamogi mutates into Speedogi (the game's real mutation graph).
        Assert.Contains("Lamogi_Speedy", data.ByPetRow("WinterSprite")!.MutationTargets);

        // Portrait rows come straight from the table.
        Assert.Equal("WinterSprite", data.ByPetRow("WinterSprite")!.CompendiumRow);
    }

    [Fact]
    public void Live_variants_include_every_table_pet()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return; // no install: skip

        var variants = PetCatalog.BuildVariants(provider);
        foreach (var friendly in new[] { "Lamogi", "Sir Ogi", "Speedogi", "Verdant Skink" })
        {
            Assert.Contains(variants, v => v.FriendlyName == friendly && v.IsEditable);
        }
        // Hostile Winter Sprite variants must not be listed.
        Assert.DoesNotContain(variants, v => v.ShortClass.Contains("WinterSprite_Bomb", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variants, v => v.ShortClass.Contains("WinterSprite_BOSS", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- reader gate: unknown rows in the Companion slot are kept ----------

    [Fact]
    public void Unknown_pet_in_companion_slot_is_not_dropped()
    {
        var player = FindAnyPlayerSave();
        if (player is null) return; // fixtures absent: skip

        var dir = Directory.CreateTempSubdirectory("pet-unknown");
        try
        {
            var copy = Path.Combine(dir.FullName, Path.GetFileName(player));
            File.Copy(player, copy);

            var data = PlayerSaveReader.ReadFromFile(copy);
            // Free the Companion slot (index 12) if a pet occupies it, then place an item
            // row no catalog knows - simulating a companion from a future game update.
            PlayerSaveWriter.RemoveCarriedPet(data, PetSlotKind.Equipment, 12);
            var placed = PlayerSaveWriter.AddCarriedPetToSlot(data, PetSlotKind.Equipment, 12,
                new CarriedPet(PetSlotKind.Equipment, 12, "Pet_FromAFutureUpdate", null, 80, 100, 42, 0, 0));
            if (placed != 12) return; // fixture has no companion slot to use: skip

            PlayerSaveWriter.WriteToFile(data, copy);

            var back = PlayerSaveReader.ReadFromFile(copy);
            var pet = back.CarriedPets.FirstOrDefault(p => p.IsCompanionSlot);
            Assert.NotNull(pet);
            Assert.Equal("Pet_FromAFutureUpdate", pet!.ItemRow);
            Assert.Equal(42, pet.Xp);
        }
        finally { dir.Delete(recursive: true); }
    }

    private static string? FindAnyPlayerSave()
    {
        var seed = Fixtures.ServerWorldsDir ?? Fixtures.CascadeDir ?? Fixtures.ClientSavedDir;
        if (seed is null) return null;
        var dir = new DirectoryInfo(seed);
        while (dir is not null && !string.Equals(dir.Name, "fixtures", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;
        if (dir is null) return null;
        foreach (var f in Directory.EnumerateFiles(dir.FullName, "Player_*.sav", SearchOption.AllDirectories))
        {
            try { PlayerSaveReader.ReadFromFile(f); return f; } catch { }
        }
        return null;
    }
}
