using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using UeSaveGame.TextData;
using static AbioticEditor.Tests.NativeSaveShapeTests;

namespace AbioticEditor.Tests;

/// <summary>
/// Edits that need something the game had not written yet: the first fish, map or recipe on a
/// character, a first kill tally, the first name for a pet, and a pet with nowhere to go.
/// </summary>
public sealed class FirstEntryAndPetTests
{
    private const string LegacyPlayer = "SteamSaves/Legacy/Cascade/PlayerData/Player_76561197993781479.sav";
    private const string NativePlayer = "Update1_5/PlayerData/Player_76561197993781479.sav";

    private static readonly string[] FirstEntryLists =
    [
        "RecipesUnlock_", "EmailsRead_", "JournalEntries_", "Compendium_EmailSections_", "Compendium_NarrativeSections_",
        "Compendium_ExplorationSections_", "Compendium_Fish_", "Compendium_KillCount_", "ItemsPickedUp_", "CraftedItems_", "MapsUnlocked_",
    ];

    /// <summary>The save with every unlock list removed, as a character that has found nothing would have it.</summary>
    private static PlayerSaveData WithoutLists(string path)
    {
        var data = PlayerSaveReader.ReadFromFile(path);
        foreach (var prefix in FirstEntryLists)
        {
            while (PlayerSaveReader.GetCharacterSaveData(data.Raw).FindByPrefix(prefix) is { } tag)
            {
                var owner = NativeSaveSchema.ResolveOwner(PlayerSaveReader.GetCharacterSaveData(data.Raw), prefix);
                owner.Remove(tag);
            }
        }
        using var stream = new MemoryStream(Bytes(data.Raw));
        return PlayerSaveReader.ReadFromStream(stream);
    }

    [SkippableTheory]
    [InlineData(LegacyPlayer)]
    [InlineData(NativePlayer)]
    public void The_first_entry_of_every_unlock_list_is_kept(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null, "fixture not in this checkout");
        var empty = WithoutLists(path!);
        Assert.Empty(empty.FishCaught);
        Assert.Empty(empty.KillCounts);
        var data = WithoutLists(path!);

        PlayerSaveWriter.ApplyRecipes(data, ["recipe_bandage"]);
        PlayerSaveWriter.ApplyEmailsRead(data, ["email_first"]);
        PlayerSaveWriter.ApplyJournals(data, ["journal_first"]);
        PlayerSaveWriter.ApplyCompendium(data, ["ce_first"], ["cn_first"], ["cx_first"]);
        PlayerSaveWriter.ApplyFishCaught(data, ["Antefish"]);
        PlayerSaveWriter.ApplyKillCounts(data, [new KillCount("Peccary", 3)]);
        PlayerSaveWriter.ApplyItemsPickedUp(data, ["scrap_metal"]);
        PlayerSaveWriter.ApplyCraftedItems(data, ["scrap_metal"]);
        PlayerSaveWriter.ApplyMapsUnlocked(data, ["map_office"]);

        if (Mappings.Value is not null) AssertNoNewShapeProblems(empty.Raw, data.Raw);
        using var stream = new MemoryStream(Bytes(data.Raw));
        var back = PlayerSaveReader.ReadFromStream(stream);
        Assert.Equal(["recipe_bandage"], back.Recipes);
        Assert.Equal(["email_first"], back.EmailsRead);
        Assert.Equal(["journal_first"], back.Journals);
        Assert.Equal(["ce_first"], back.CompendiumEmail);
        Assert.Equal(["cn_first"], back.CompendiumNarrative);
        Assert.Equal(["cx_first"], back.CompendiumExploration);
        Assert.Equal(["Antefish"], back.FishCaught);
        Assert.Equal([new KillCount("Peccary", 3)], back.KillCounts);
        Assert.Equal(["scrap_metal"], back.ItemsPickedUp);
        Assert.Equal(["scrap_metal"], back.CraftedItems);
        Assert.Equal(["map_office"], back.MapsUnlocked);
    }

    [SkippableTheory]
    [InlineData(LegacyPlayer)]
    [InlineData(NativePlayer)]
    public void Empty_lists_are_not_created_for_nothing(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null, "fixture not in this checkout");
        var data = WithoutLists(path!);
        var before = Bytes(data.Raw);

        PlayerSaveWriter.ApplyRecipes(data, []);
        PlayerSaveWriter.ApplyFishCaught(data, []);
        PlayerSaveWriter.ApplyMapsUnlocked(data, []);
        PlayerSaveWriter.ApplyKillCounts(data, [new KillCount("Peccary", 0)]);

        Assert.Equal(before, Bytes(data.Raw));
    }

    [SkippableFact]
    public void A_first_kill_is_added_beside_existing_tallies()
    {
        var path = Fixture(LegacyPlayer);
        Skip.If(path is null, "fixture not in this checkout");
        var data = PlayerSaveReader.ReadFromFile(path!);
        var existing = data.KillCounts;
        Assert.NotEmpty(existing);
        var fresh = existing.Select(k => k.CompendiumRow).Contains("Audit_Creature", StringComparer.Ordinal) ? "Audit_Creature2" : "Audit_Creature";

        PlayerSaveWriter.ApplyKillCounts(data, [.. existing, new KillCount(fresh, 4)]);

        if (Mappings.Value is not null) AssertNoNewShapeProblems(PlayerSaveReader.ReadFromFile(path!).Raw, data.Raw);
        using var stream = new MemoryStream(Bytes(data.Raw));
        var back = PlayerSaveReader.ReadFromStream(stream);
        Assert.Equal([.. existing, new KillCount(fresh, 4)], back.KillCounts);
    }

    [SkippableFact]
    public void Codex_offers_a_tally_for_creatures_never_killed()
    {
        var path = Fixture(NativePlayer);
        Skip.If(path is null, "fixture not in this checkout");
        var data = PlayerSaveReader.ReadFromFile(path!);
        Assert.Empty(data.KillCounts);
        var vocabulary = new CodexVocabulary([], [], [new CompendiumEntry("Peccary", "Peccary", null, "Creature", [], [], 5)], []);

        var codex = PlayerCodexEdit.Create(data, vocabulary);

        var row = Assert.Single(codex.Compendium);
        Assert.Equal(0, row.KillCount);
        row.KillCount = 2;
        PlayerSaveWriter.ApplyKillCounts(data, codex.CurrentKills().Select(k => new KillCount(k.Key, k.Value)).ToList());
        using var stream = new MemoryStream(Bytes(data.Raw));
        Assert.Equal([new KillCount("Peccary", 2)], PlayerSaveReader.ReadFromStream(stream).KillCounts);
    }

    /// <summary>
    /// A native world with two pets the way the game writes them: one named with a Base text,
    /// one never named (the game leaves the name out).
    /// </summary>
    private static WorldSaveData NativeWorldWithPets(string path)
    {
        var data = WorldSaveReader.ReadFromFile(path);
        var module = new FPropertyTypeName(new FString("/Script/AbioticFactor"));
        var stateType = new FPropertyTypeName(new FString("AbioticNPCStateSave"), [module]);
        var keyType = new FPropertyTypeName(new FString(nameof(StrProperty)));
        var valueType = new FPropertyTypeName(new FString(nameof(StructProperty)), [stateType]);
        var name = new FString("PetNPCMap");
        var map = new MapProperty(name) { KeyType = keyType, ValueType = valueType, Value = new List<KeyValuePair<FProperty, FProperty>>() };

        StructProperty Pet(string? petName)
        {
            var body = new PropertiesStruct { Properties = new List<FPropertyTag>() };
            body.Properties.Add(new FPropertyTag(new FString("bDead"), new FPropertyTypeName(new FString(nameof(BoolProperty))), EPropertyTagFlags.None)
                { Property = new BoolProperty(new FString("bDead")) { Value = false } });
            if (petName is not null)
            {
                var text = new FText { HistoryType = TextHistoryType.Base, Value = new TextData_Base { Namespace = new FString(""), Key = new FString("6F1C2D7A4B3E9C80"), SourceString = new FString(petName) } };
                body.Properties.Add(new FPropertyTag(new FString("CustomName"), new FPropertyTypeName(new FString(nameof(TextProperty))), EPropertyTagFlags.None)
                    { Property = new TextProperty(new FString("CustomName")) { Value = text } });
            }
            return new StructProperty(name) { StructType = stateType, StructGuid = Guid.Empty, Value = body };
        }

        map.Value.Add(new(new StrProperty(name) { Value = new FString("named-pet") }, Pet("Old Name")));
        map.Value.Add(new(new StrProperty(name) { Value = new FString("unnamed-pet") }, Pet(null)));
        data.Raw.Properties!.Insert(data.Raw.Properties.Count - 2, new FPropertyTag(name, new FPropertyTypeName(new FString(nameof(MapProperty)), [keyType, valueType]), EPropertyTagFlags.None) { Property = map });
        using var stream = new MemoryStream(Bytes(data.Raw));
        return WorldSaveReader.ReadFromStream(stream);
    }

    [SkippableFact]
    public void Native_pets_can_be_renamed_and_named_for_the_first_time()
    {
        var path = Fixture("Update1_5/PlacedPets/WorldSave_Facility.sav");
        Skip.If(path is null, "fixture not in this checkout");
        var data = NativeWorldWithPets(path!);
        Assert.Equal("Old Name", data.Pets.Single(p => p.Id == "named-pet").CustomName);
        Assert.Null(data.Pets.Single(p => p.Id == "unnamed-pet").CustomName);
        var before = Bytes(data.Raw);

        WorldSaveWriter.ApplyPets(data, data.Pets);
        Assert.Equal(before, Bytes(data.Raw));

        WorldSaveWriter.ApplyPets(data, data.Pets.Select(p => p with { CustomName = p.Id == "named-pet" ? "Bristles" : "Snout" }).ToList());
        using var stream = new MemoryStream(Bytes(data.Raw));
        var back = WorldSaveReader.ReadFromStream(stream);
        Assert.Equal("Bristles", back.Pets.Single(p => p.Id == "named-pet").CustomName);
        Assert.Equal("Snout", back.Pets.Single(p => p.Id == "unnamed-pet").CustomName);
    }

    [SkippableFact]
    public void A_pet_with_no_free_slot_is_refused_without_touching_the_save()
    {
        var path = Fixture(NativePlayer);
        Skip.If(path is null, "fixture not in this checkout");
        var data = PlayerSaveReader.ReadFromFile(path!);
        var full = new Func<IReadOnlyList<InventoryItemSlot>, List<InventoryItemSlot>>(slots =>
            slots.Select(s => s.IsEmpty ? s with { ItemId = "scrap_metal", Count = 1 } : s).ToList());
        PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Equipment = full(data.Inventory.Equipment), Hotbar = full(data.Inventory.Hotbar) });
        using var filled = new MemoryStream(Bytes(data.Raw));
        data = PlayerSaveReader.ReadFromStream(filled);
        var before = Bytes(data.Raw);

        var index = PlayerSaveWriter.AddCarriedPetToSlot(data, PetSlotKind.Hotbar, -1, new CarriedPet(PetSlotKind.Hotbar, -1, "Peccary", "Nowhere", 100, 100, 0, 0, 0));

        Assert.Equal(-1, index);
        Assert.Equal(before, Bytes(data.Raw));
        var session = new PlayerSaveSession(data, path!);
        Assert.False(session.TryAddCatalogPet("Peccary", PetSlotKind.Equipment, "Nowhere", out var message));
        Assert.Contains("full", message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.IsDirty);
    }

    [SkippableFact]
    public void A_carried_pet_without_mutation_entries_is_left_as_the_game_wrote_it()
    {
        var path = Fixture("Update1_5/Validation/Player_76561197993781479.sav");
        Skip.If(path is null, "fixture not in this checkout");
        var data = PlayerSaveReader.ReadFromFile(path!);
        var pet = Assert.Single(data.CarriedPets);
        var slots = Assert.IsType<ArrayProperty>(PlayerSaveReader.GetCharacterSaveData(data.Raw).FindByPrefix("EquipmentInventory_")!.Property).Value!;
        var changeable = (PropertiesStruct)((StructProperty)((PropertiesStruct)((StructProperty)slots.GetValue(pet.Index)!).Value!)
            .Properties.FindByPrefix("ChangeableData_")!.Property!).Value!;
        var dynamic = Assert.IsType<ArrayProperty>(changeable.Properties.FindByPrefix("DynamicProperties_")!.Property);
        // Older and converted saves leave zero-valued pet entries out entirely.
        dynamic.Value = dynamic.Value!.Cast<StructProperty>()
            .Where(e => ((PropertiesStruct)e.Value!).Properties.FindByPrefix("Key")!.Property!.Value!.ToString() is not ("EDynamicProperty::MutationProgress" or "EDynamicProperty::PetMutation"))
            .ToArray<FProperty>();
        using var trimmed = new MemoryStream(Bytes(data.Raw));
        data = PlayerSaveReader.ReadFromStream(trimmed);
        var before = Bytes(data.Raw);

        PlayerSaveWriter.ApplyCarriedPet(data, Assert.Single(data.CarriedPets));

        Assert.Equal(before, Bytes(data.Raw));
    }

    [Fact]
    public void A_save_refused_for_its_layout_explains_why_instead_of_blaming_permissions()
    {
        var languages = new HostLanguageService();
        var refusal = new SaveWriteBlockedException("This save was written by a newer version of the game.", CompatibilitySeverity.LayoutChanged);

        Assert.Equal(refusal.Message, SaveFailureGuidance.For(refusal, languages));
        var unexpected = SaveFailureGuidance.For(new InvalidCastException("internal"), languages);
        Assert.DoesNotContain("permission", unexpected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("read-only", unexpected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal", unexpected, StringComparison.Ordinal);
        Assert.Equal(languages.Resource("Workspace_SaveFailedDenied"), SaveFailureGuidance.For(new UnauthorizedAccessException(), languages));
    }
}
