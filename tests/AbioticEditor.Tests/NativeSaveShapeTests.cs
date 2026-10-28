using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>
/// Runs every save writer over game-written saves and checks the result against the game's own
/// type mappings (field names, types and enum values), so a writer can never again put a shape
/// into a save that the game does not expect. Re-applying what was read must not change a byte.
/// </summary>
public sealed class NativeSaveShapeTests
{
    public static TheoryData<string> PlayerSaves => new()
    {
        "Update1_5/PlayerData/Player_76561197993781479.sav",
        "Update1_5/Validation/Player_76561197993781479.sav",
        "Update1_5/PlacedPets/Player_76561197993781479.sav",
        "SteamSaves/Legacy/Cascade/PlayerData/Player_76561197993781479.sav",
    };

    public static TheoryData<string> WorldSaves => new()
    {
        "Update1_5/WorldSave_Facility.sav",
        "Update1_5/WorldSave_MetaData.sav",
        "Update1_5/PlacedPets/WorldSave_Facility.sav",
        "SteamSaves/Legacy/Cascade/WorldSave_Facility.sav",
        "SteamSaves/Legacy/Cascade/WorldSave_MetaData.sav",
    };

    private static readonly Lazy<TypeMappings?> Mappings = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "assets", "Mappings.usmap");
            if (File.Exists(path)) return new FileUsmapTypeMappingsProvider(path).MappingsForGame;
        }
        return null;
    });

    private static string? Fixture(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "fixtures", relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static byte[] Bytes(SaveGame save)
    {
        using var stream = new MemoryStream();
        save.WriteTo(stream);
        return stream.ToArray();
    }

    [SkippableTheory]
    [MemberData(nameof(PlayerSaves))]
    public void Unchanged_player_saves_come_back_byte_for_byte(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null, "fixture not in this checkout");
        var d = PlayerSaveReader.ReadFromFile(path!);
        PlayerSaveWriter.ApplyStats(d, d.Stats);
        PlayerSaveWriter.ApplyLimbHealth(d, d.Health);
        PlayerSaveWriter.ApplySkills(d, d.Skills);
        PlayerSaveWriter.ApplyRecipes(d, d.Recipes);
        PlayerSaveWriter.ApplyTraits(d, d.Traits);
        if (d.Phd is { } phd) PlayerSaveWriter.ApplyPhd(d, phd);
        PlayerSaveWriter.ApplyItemsPickedUp(d, d.ItemsPickedUp);
        PlayerSaveWriter.ApplyCraftedItems(d, d.CraftedItems);
        PlayerSaveWriter.ApplyItemsDistilled(d, d.ItemsDistilled);
        PlayerSaveWriter.ApplyActiveBuffs(d, d.ActiveBuffs);
        PlayerSaveWriter.ApplyMapsUnlocked(d, d.MapsUnlocked);
        PlayerSaveWriter.ApplyInventory(d, d.Inventory);
        PlayerSaveWriter.ApplyTransmogSlots(d, d.TransmogSlots);
        PlayerSaveWriter.ApplyTransmogVisibility(d, d.TransmogVisibility);
        PlayerSaveWriter.ApplyTransmogDisabled(d, d.TransmogDisabled);
        foreach (var pet in d.CarriedPets) PlayerSaveWriter.ApplyCarriedPet(d, pet);
        PlayerSaveWriter.ApplyEmailsRead(d, d.EmailsRead);
        PlayerSaveWriter.ApplyJournals(d, d.Journals);
        PlayerSaveWriter.ApplyCompendium(d, d.CompendiumEmail, d.CompendiumNarrative, d.CompendiumExploration);
        PlayerSaveWriter.ApplyFishCaught(d, d.FishCaught);
        PlayerSaveWriter.ApplyKillCounts(d, d.KillCounts);
        PlayerSaveWriter.ApplyResearchQueue(d, d.RecipesRequiringResearch);
        PlayerSaveWriter.ApplyCompletedIntro(d, d.CompletedIntro);

        Assert.Equal(File.ReadAllBytes(path!), Bytes(d.Raw));
    }

    [SkippableTheory]
    [MemberData(nameof(WorldSaves))]
    public void Unchanged_world_saves_come_back_byte_for_byte(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null, "fixture not in this checkout");
        var d = WorldSaveReader.ReadFromFile(path!);
        WorldSaveWriter.ApplyContainers(d, d.Containers);
        WorldSaveWriter.ApplyFlags(d, d.Flags);
        WorldSaveWriter.ApplyDoors(d, d.Doors);
        if (d.StoryProgressionRow is { } row) WorldSaveWriter.ApplyStoryProgression(d, row);
        if (d.MinutesPassed is { } minutes) WorldSaveWriter.ApplyMinutesPassed(d, minutes);
        if (d.GlobalRecipes.Count > 0) WorldSaveWriter.ApplyGlobalRecipes(d, d.GlobalRecipes);
        WorldSaveWriter.ApplyDroppedItems(d, d.DroppedItems);
        WorldSaveWriter.ApplyNpcs(d, d.Npcs);
        WorldSaveWriter.ApplyPets(d, d.Pets);
        WorldSaveWriter.ApplyVehicles(d, d.Vehicles);

        Assert.Equal(File.ReadAllBytes(path!), Bytes(d.Raw));
    }

    [SkippableTheory]
    [MemberData(nameof(PlayerSaves))]
    public void Edited_player_saves_keep_the_games_own_shape(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null || Mappings.Value is null, "fixture or game mappings not in this checkout");
        var original = PlayerSaveReader.ReadFromFile(path!);
        var d = PlayerSaveReader.ReadFromFile(path!);

        PlayerSaveWriter.ApplyStats(d, d.Stats with { Money = d.Stats.Money + 7, Hunger = 50 });
        PlayerSaveWriter.ApplyLimbHealth(d, new LimbHealth(50, 51, 52, 53, 54, 55));
        PlayerSaveWriter.ApplySkills(d, d.Skills.Select(s => s with { Xp = s.Xp + 10 }).ToList());
        PlayerSaveWriter.ApplyRecipes(d, [.. d.Recipes, "recipe_shape"]);
        PlayerSaveWriter.ApplyTraits(d, [.. d.Traits, "Trait_Hearty"]);
        PlayerSaveWriter.ApplyPhd(d, "PhD_HumanBio");
        PlayerSaveWriter.ApplyItemsPickedUp(d, [.. d.ItemsPickedUp, "item_shape"]);
        PlayerSaveWriter.ApplyCraftedItems(d, [.. d.CraftedItems, "item_shape"]);
        PlayerSaveWriter.ApplyItemsDistilled(d, [.. d.ItemsDistilled, "item_shape"]);
        PlayerSaveWriter.ApplyEmailsRead(d, [.. d.EmailsRead, "email_shape"]);
        PlayerSaveWriter.ApplyJournals(d, [.. d.Journals, "journal_shape"]);
        PlayerSaveWriter.ApplyCompendium(d, [.. d.CompendiumEmail, "ce_shape"], [.. d.CompendiumNarrative, "cn_shape"], [.. d.CompendiumExploration, "cx_shape"]);
        PlayerSaveWriter.ApplyFishCaught(d, [.. d.FishCaught, "fish_shape"]);
        PlayerSaveWriter.ApplyKillCounts(d, d.KillCounts.Select(k => k with { Count = k.Count + 1 }).ToList());
        PlayerSaveWriter.ApplyResearchQueue(d, [.. d.RecipesRequiringResearch, "recipe_research_shape"]);
        PlayerSaveWriter.ApplyCompletedIntro(d, !d.CompletedIntro);
        PlayerSaveWriter.ApplyLastControlRotation(d, 1, 2, 3);
        PlayerSaveWriter.ApplyRespawn(d, d.RespawnX + 1, d.RespawnY, d.RespawnZ, Guid.NewGuid().ToString("N").ToUpperInvariant());
        PlayerSaveWriter.ApplyRespawnTerminal(d, Guid.NewGuid().ToString("N").ToUpperInvariant());
        PlayerSaveWriter.ApplyTransmogVisibility(d, d.TransmogVisibility.Select(v => !v).ToList());
        PlayerSaveWriter.ApplyLastHotbarSelection(d, 1);
        var main = d.Inventory.Main.ToArray();
        var empty = Array.FindIndex(main, slot => slot.IsEmpty);
        if (empty >= 0) main[empty] = main[empty] with { ItemId = "scrap_metal", Count = 5 };
        PlayerSaveWriter.ApplyInventory(d, d.Inventory with { Main = main });
        PlayerSaveWriter.AddCarriedPetToSlot(d, PetSlotKind.Hotbar, -1, new CarriedPet(PetSlotKind.Hotbar, -1, "Peccary", "Shape", 100, 100, 10, 1, 0));

        AssertNoNewShapeProblems(original.Raw, d.Raw);
        using var stream = new MemoryStream(Bytes(d.Raw));
        var back = PlayerSaveReader.ReadFromStream(stream);
        Assert.Equal(original.Stats.Money + 7, back.Stats.Money);
        Assert.Contains("Trait_Hearty", back.Traits);
        Assert.Contains("recipe_shape", back.Recipes);
        Assert.Contains("item_shape", back.ItemsDistilled);
        Assert.Contains("journal_shape", back.Journals);
        Assert.Equal("PhD_HumanBio", back.Phd);
        if (empty >= 0) Assert.Contains(back.Inventory.Main, slot => slot.ItemId == "scrap_metal" && slot.Count == 5);
    }

    [SkippableTheory]
    [MemberData(nameof(WorldSaves))]
    public void Edited_world_saves_keep_the_games_own_shape(string relative)
    {
        var path = Fixture(relative);
        Skip.If(path is null || Mappings.Value is null, "fixture or game mappings not in this checkout");
        var original = WorldSaveReader.ReadFromFile(path!);
        var d = WorldSaveReader.ReadFromFile(path!);

        WorldSaveWriter.ApplyContainers(d, d.Containers.Select(c => c with
        {
            Name = "Shape",
            Inventories = c.Inventories.Select(inv => new WorldInventory(inv.Slots.Select((slot, i) => i != 0 ? slot
                : slot.IsEmpty ? slot with { ItemId = "scrap_metal", Count = 3 } : slot with { Count = slot.Count + 1 }).ToList())).ToList(),
        }).ToList());
        WorldSaveWriter.ApplyFlags(d, [.. d.Flags, "Shape_Flag"]);
        WorldSaveWriter.ApplyDoors(d, d.Doors.Select(x => x with { NoReset = !(x.NoReset ?? false), IsDoorOpen = x.IsDoorOpen is null ? null : !x.IsDoorOpen }).ToList());
        WorldSaveWriter.ApplyMinutesPassed(d, (d.MinutesPassed ?? 0) + 5);
        WorldSaveWriter.ApplyGlobalRecipes(d, [.. d.GlobalRecipes, "recipe_shape"]);
        WorldSaveWriter.ApplyDroppedItems(d, d.DroppedItems.Select(x => x with { NoDespawn = !x.NoDespawn, Slot = x.Slot with { Count = x.Slot.Count + 1 } }).ToList());
        WorldSaveWriter.AddDroppedItem(d, new InventoryItemSlot(0, "scrap_metal", 2, 0, 0, 0, 0, null, false, null, null), 1, 2, 3);
        WorldSaveWriter.ApplyNpcs(d, d.Npcs.Select(n => n with { IsDead = !n.IsDead }).ToList());
        WorldSaveWriter.ApplyPets(d, d.Pets.Select(p => p with { Xp = p.Xp + 3, CustomName = "Shape" }).ToList());
        WorldSaveWriter.ApplyVehicles(d, d.Vehicles.Select(v => v with { Destroyed = !v.Destroyed }).ToList());
        WorldSaveWriter.ApplyWorldClock(d, 3600, 4);
        WorldSaveWriter.ApplyDayDiscovered(d, 3);

        AssertNoNewShapeProblems(original.Raw, d.Raw);
        using var stream = new MemoryStream(Bytes(d.Raw));
        var back = WorldSaveReader.ReadFromStream(stream);
        if (original.MinutesPassed is not null) Assert.Equal(original.MinutesPassed + 5, back.MinutesPassed);
        if (original.Containers.Count > 0) Assert.Contains(back.Containers, c => c.Inventories.Any(inv => inv.Slots.Count > 0 && !inv.Slots[0].IsEmpty));
        if (original.DroppedItems.Count > 0) Assert.Contains(back.DroppedItems, x => x.Slot.ItemId == "scrap_metal" && Math.Abs(x.X - 1) < 0.01);
        Assert.All(back.Pets, p => Assert.Equal("Shape", p.CustomName));
    }

    [SkippableFact]
    public void Native_world_reads_containers_dropped_items_and_deployable_classes()
    {
        var path = Fixture("Update1_5/PlacedPets/WorldSave_Facility.sav");
        Skip.If(path is null, "fixture not in this checkout");
        var data = WorldSaveReader.ReadFromFile(path!);

        Assert.Contains(data.Containers, c => c.Inventories.SelectMany(i => i.Slots).Any(s => !s.IsEmpty));
        Assert.All(data.DroppedItems, x => Assert.False(x.Slot.IsEmpty));
        Assert.All(data.Deployables, x => Assert.NotNull(x.ClassName));
    }

    [SkippableFact]
    public void Untouched_empty_slots_keep_the_marker_the_game_wrote()
    {
        var path = Fixture("Update1_5/PlayerData/Player_76561197993781479.sav");
        Skip.If(path is null, "fixture not in this checkout");
        // Native saves migrated from older ones mark empty slots "Empty" rather than "None".
        var data = PlayerSaveReader.ReadFromFile(path!);
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var slots = Assert.IsType<ArrayProperty>(root.FindByPrefix("Inventory_")!.Property).Value!;
        var index = Enumerable.Range(0, slots.Length).First(i => data.Inventory.Main[i].IsEmpty);
        var handle = ((PropertiesStruct)((StructProperty)((PropertiesStruct)((StructProperty)slots.GetValue(index)!).Value!)
            .Properties.FindByPrefix("ItemDataTable_")!.Property!).Value!).Properties;
        handle.FindByPrefix("RowName")!.Property!.Value = new FString("Empty");
        using var migrated = new MemoryStream(Bytes(data.Raw));
        data = PlayerSaveReader.ReadFromStream(migrated);
        var before = Bytes(data.Raw);

        PlayerSaveWriter.ApplyInventory(data, data.Inventory);

        Assert.Equal(before, Bytes(data.Raw));
    }

    private static void AssertNoNewShapeProblems(SaveGame original, SaveGame edited)
    {
        var known = GameShape.Problems(original, Mappings.Value!).ToHashSet(StringComparer.Ordinal);
        var added = GameShape.Problems(edited, Mappings.Value!).Where(p => !known.Contains(p)).Distinct().ToList();
        Assert.True(added.Count == 0, "Edits wrote shapes the game does not expect:\n" + string.Join("\n", added));
        using var stream = new MemoryStream(Bytes(edited));
        SaveGame.LoadFrom(stream);
    }

    /// <summary>Compares a loaded save tree with the game's type mappings, from each mapped struct down.</summary>
    private static class GameShape
    {
        public static List<string> Problems(SaveGame save, TypeMappings tm)
        {
            var problems = new List<string>();
            foreach (var tag in save.Properties!)
            {
                var t = tag.Type;
                var structName = t.Name.Value switch
                {
                    "StructProperty" => t.Parameters.FirstOrDefault()?.Name.Value,
                    "ArrayProperty" or "SetProperty" when t.Parameters.FirstOrDefault()?.Name.Value == "StructProperty" => t.Parameters[0].Parameters.FirstOrDefault()?.Name.Value,
                    "MapProperty" when t.Parameters.Count == 2 && t.Parameters[1].Name.Value == "StructProperty" => t.Parameters[1].Parameters.FirstOrDefault()?.Name.Value,
                    _ => null,
                };
                if (structName is null || !tm.Types.ContainsKey(structName)) continue;
                var element = new PropertyType("StructProperty", structName);
                var shape = t.Name.Value switch
                {
                    "StructProperty" => element,
                    "MapProperty" => new PropertyType("MapProperty", null, new PropertyType(t.Parameters[0].Name.Value), element),
                    _ => new PropertyType(t.Name.Value, null, element),
                };
                Value(tag.Property, shape, "/" + tag.Name?.Value, tm, problems);
            }
            return problems;
        }

        private static PropertyInfo? Find(TypeMappings tm, string structName, string name)
        {
            for (var s = tm.Types.GetValueOrDefault(structName); s is not null; s = s.SuperType is null ? null : tm.Types.GetValueOrDefault(s.SuperType))
            {
                foreach (var p in s.Properties.Values)
                {
                    if (p.Name == name) return p;
                }
            }
            return null;
        }

        private static void Props(IList<FPropertyTag> props, string structName, string path, TypeMappings tm, List<string> problems)
        {
            if (!tm.Types.ContainsKey(structName)) return;
            foreach (var tag in props)
            {
                var here = $"{path}/{tag.Name?.Value}";
                var info = Find(tm, structName, tag.Name?.Value ?? string.Empty);
                if (info is null)
                {
                    problems.Add($"{here}: not a field of {structName}");
                    continue;
                }
                if (Mismatch(tag.Type, info.MappingType) is { } why) problems.Add($"{here}: {why}");
                Value(tag.Property, info.MappingType, here, tm, problems);
            }
        }

        private static string? Mismatch(FPropertyTypeName t, PropertyType m)
        {
            var enumOrByte = t.Name.Value is "ByteProperty" or "EnumProperty" && m.Type is "ByteProperty" or "EnumProperty";
            if (t.Name.Value != m.Type && !enumOrByte) return $"is {t.Name.Value}, game expects {m.Type}";
            return m.Type switch
            {
                "StructProperty" when t.Parameters.Count == 0 || t.Parameters[0].Name.Value != m.StructType
                    => $"is struct {t.Parameters.FirstOrDefault()?.Name.Value}, game expects {m.StructType}",
                "ArrayProperty" or "SetProperty" when t.Parameters.Count > 0 && m.InnerType is not null
                    => Mismatch(t.Parameters[0], m.InnerType) is { } inner ? $"element {inner}" : null,
                "MapProperty" when t.Parameters.Count == 2 && m.InnerType is not null && m.ValueType is not null
                    => (Mismatch(t.Parameters[0], m.InnerType) is { } k ? $"key {k}" : null)
                       ?? (Mismatch(t.Parameters[1], m.ValueType) is { } v ? $"value {v}" : null),
                _ => null,
            };
        }

        private static void Value(FProperty? p, PropertyType m, string path, TypeMappings tm, List<string> problems)
        {
            if (m.EnumName is not null && p is EnumProperty or ByteProperty && p.Value is FString or string)
            {
                var text = p.Value!.ToString()!;
                var split = text.IndexOf("::", StringComparison.Ordinal);
                var enumName = split < 0 ? m.EnumName : text[..split];
                var member = split < 0 ? text : text[(split + 2)..];
                if (enumName != m.EnumName || tm.Enums.TryGetValue(m.EnumName, out var members) && !members.ContainsValue(member))
                    problems.Add($"{path}: '{text}' is not a value of {m.EnumName}");
            }
            switch (p)
            {
                case StructProperty { Value: PropertiesStruct ps } when m.StructType is not null:
                    Props(ps.Properties, m.StructType, path, tm, problems);
                    break;
                case ArrayProperty { Value: { } values } a when m.InnerType is not null:
                    var structItems = a.ItemType?.Name.Value == "StructProperty";
                    foreach (var v in values)
                    {
                        if (structItems != v is StructProperty)
                        {
                            problems.Add($"{path}[]: element is {v?.GetType().Name}");
                            break;
                        }
                        if (v is StructProperty { Value: PropertiesStruct eps } && m.InnerType.StructType is not null)
                            Props(eps.Properties, m.InnerType.StructType, path + "[]", tm, problems);
                    }
                    break;
                case MapProperty { Value: { } pairs }:
                    foreach (var pair in pairs)
                    {
                        if (m.InnerType is not null) Value(pair.Key, m.InnerType, path + "{key}", tm, problems);
                        if (m.ValueType is not null) Value(pair.Value, m.ValueType, path + "{value}", tm, problems);
                    }
                    break;
            }
        }
    }
}
