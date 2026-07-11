using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Parses an Abiotic Factor <c>Player_*.sav</c> file into typed models.
///
/// AF property names carry hash-suffixed signatures from the blueprint compiler - e.g.
/// <c>Hunger_2_A6C5CC6E41993323B119FA9E0B3894CA</c>. We match by prefix so the reader is
/// resilient to suffix changes between game patches.
/// </summary>
public static class PlayerSaveReader
{
    static PlayerSaveReader()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Loads a player save from <paramref name="path"/> and returns a typed view.
    /// Throws if the file isn't an Abiotic Character save.
    /// </summary>
    public static PlayerSaveData ReadFromFile(string path)
    {
        Diagnostics.EditorLog.Info("PlayerSave", $"Parsing {Path.GetFileName(path)}");
        try
        {
            using var fs = File.OpenRead(path);
            return ReadFromStream(fs);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("PlayerSave", $"Failed to parse {Path.GetFileName(path)}", ex);
            throw;
        }
    }

    public static PlayerSaveData ReadFromStream(Stream stream)
        => ReadFrom(SaveGame.LoadFrom(stream));

    /// <summary>
    /// Builds the typed model over an already-loaded <see cref="SaveGame"/>. Lets callers
    /// that already hold a save (the editor's open document, a plugin save operation) read
    /// it without a second parse, and - because the returned data's <c>Raw</c> IS the passed
    /// save - lets <see cref="PlayerSaveWriter"/>'s Apply* methods mutate that same instance
    /// in place.
    /// </summary>
    public static PlayerSaveData ReadFrom(SaveGame save)
    {
        var root = GetCharacterSaveData(save);

        var stats = ReadStats(root);
        var inventory = new PlayerInventory(
            Equipment: ReadInventoryArray(root, "EquipmentInventory_"),
            Hotbar: ReadInventoryArray(root, "HotbarInventory_"),
            Main: ReadInventoryArray(root, "Inventory_"));

        var respawn = ReadVector(root, "LastSafeWorldLocation_");

        var data = new PlayerSaveData(
            save, stats, inventory,
            skills: ReadSkills(root),
            traits: ReadNameArray(root, "Traits_"),
            phd: root.GetString("PhD_"),
            health: ReadLimbHealth(root),
            recipes: ReadNameArray(root, "RecipesUnlock_"),
            emailsRead: ReadNameArray(root, "EmailsRead_"),
            journals: ReadNameArray(root, "JournalEntries_"),
            compendiumEmail: ReadNameArray(root, "Compendium_EmailSections_"),
            compendiumNarrative: ReadNameArray(root, "Compendium_NarrativeSections_"),
            compendiumExploration: ReadNameArray(root, "Compendium_ExplorationSections_"),
            itemsPickedUp: ReadNameArray(root, "ItemsPickedUp_"),
            craftedItems: ReadNameArray(root, "CraftedItems_"),
            mapsUnlocked: ReadNameArray(root, "MapsUnlocked_"),
            killCounts: ReadKillCounts(root),
            fishCaught: ReadNameArray(root, "Compendium_Fish_"),
            transmogSlots: ReadInventoryArray(root, "TransmogInventory_"),
            transmogVisibility: ReadBoolArray(root, "TransmogVisibility_"),
            respawnX: respawn.X,
            respawnY: respawn.Y,
            respawnZ: respawn.Z,
            respawnLevelGuid: root.GetString("LastSafeWorldGUID_"),
            terminalRespawnId: root.GetString("TerminalRespawnID_"),
            carriedPets: ReadCarriedPets(root));

        LogUnmodeledKeys(root);
        return data;
    }

    // CharacterSaveData prefixes the reader consumes. Anything else is data the editor
    // has NO visibility on - logged so format drift across game updates is traceable.
    private static readonly string[] ConsumedPrefixes =
    {
        "CurrentSurvivalStats_", "CurrentMoney_",
        "EquipmentInventory_", "HotbarInventory_", "Inventory_",
        "Skills_", "Traits_", "PhD_", "CharacterHealth_",
        "RecipesUnlock_", "EmailsRead_", "JournalEntries_",
        "Compendium_EmailSections_", "Compendium_NarrativeSections_", "Compendium_ExplorationSections_",
        "Compendium_KillCount_", "Compendium_Fish_",
        "ItemsPickedUp_", "CraftedItems_", "MapsUnlocked_",
        "TransmogInventory_", "TransmogVisibility_",
        "LastSafeWorldLocation_", "LastSafeWorldGUID_", "TerminalRespawnID_",
        // Understood bookkeeping, intentionally preserved rather than edited: unread
        // markers for codex content, UI slot favorites, distillery history, the
        // newest-recipe toast list, research queue, per-slot transmog disables, the
        // intro-cinematic bool and the last camera rotation.
        "Compendium_Unread_", "Fish_Unread_", "Journal_Unread_",
        "FavoritedSlots_", "ItemsDistilled_", "NewestRecipes_",
        "RecipesRequiringResearch_", "TransmogDisabledArray_",
        "CompletedIntro_", "LastControlRotation_",
    };

    private static void LogUnmodeledKeys(IList<FPropertyTag> root)
    {
        foreach (var tag in root)
        {
            var name = tag.Name?.Value;
            if (name is null) continue;
            if (ConsumedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            Diagnostics.EditorLog.UnknownData("PlayerSave", name,
                $"unmodeled CharacterSaveData property ({tag.Property?.GetType().Name ?? "?"}) - preserved verbatim, not editable in the UI");
        }
    }

    /// <summary>
    /// Reads a top-level Vector StructProperty (e.g. <c>LastSafeWorldLocation_</c>).
    /// Unlike <c>Transform_.Translation</c> in world saves, the player save stores the
    /// vector directly as the struct value.
    /// </summary>
    private static (double X, double Y, double Z) ReadVector(IList<FPropertyTag> root, string prefix)
    {
        if (root.FindByPrefix(prefix)?.Property is StructProperty sp && sp.Value is VectorStruct vec)
        {
            return (vec.Value.X, vec.Value.Y, vec.Value.Z);
        }
        return (0, 0, 0);
    }

    /// <summary>Reads an ArrayProperty of Bool (stored as a plain <c>bool[]</c>).</summary>
    private static IReadOnlyList<bool> ReadBoolArray(IList<FPropertyTag> root, string prefix)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<bool>();
        }

        var result = new List<bool>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            result.Add(array.Value.GetValue(i) is true);
        }
        return result;
    }

    /// <summary>
    /// Reads <c>Compendium_KillCount_</c>: an array of <c>CompendiumKillCount</c> structs,
    /// each holding <c>CompendiumRow.RowName</c> and an int <c>Count</c>.
    /// </summary>
    private static IReadOnlyList<KillCount> ReadKillCounts(IList<FPropertyTag> root)
    {
        var tag = root.FindByPrefix("Compendium_KillCount_");
        if (tag?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<KillCount>();
        }

        var result = new List<KillCount>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            string? row = null;
            if (ps.Properties.FindByPrefix("CompendiumRow")?.Property is StructProperty rowSp
                && rowSp.Value is PropertiesStruct rowPs)
            {
                row = rowPs.Properties.GetString("RowName");
            }
            var count = (int)ps.Properties.GetLong("Count");
            if (!string.IsNullOrEmpty(row))
            {
                result.Add(new KillCount(row!, count));
            }
        }
        return result;
    }

    private static LimbHealth ReadLimbHealth(IList<FPropertyTag> root)
    {
        var tag = root.FindByPrefix("CharacterHealth_");
        if (tag?.Property is not StructProperty sp || sp.Value is not PropertiesStruct ps)
        {
            return LimbHealth.Full;
        }

        var p = ps.Properties;
        return new LimbHealth(
            Head: p.GetDouble("Head_", 100),
            Torso: p.GetDouble("Torso_", 100),
            LeftArm: p.GetDouble("LeftArm_", 100),
            RightArm: p.GetDouble("RightArm_", 100),
            LeftLeg: p.GetDouble("LeftLeg_", 100),
            RightLeg: p.GetDouble("RightLeg_", 100));
    }

    /// <summary>
    /// Reads the positional <c>Skills_</c> array. Each element is an
    /// <c>Abiotic_CharacterSkill_Struct</c>; only <c>CurrentSkillXP_</c> and
    /// <c>CurrentXPMultiplier_</c> are meaningful - the embedded SkillName text is the
    /// blueprint default for every entry (identity is the array index).
    /// </summary>
    private static IReadOnlyList<PlayerSkill> ReadSkills(IList<FPropertyTag> root)
    {
        var tag = root.FindByPrefix("Skills_");
        if (tag?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<PlayerSkill>();
        }

        var result = new List<PlayerSkill>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            float xp = 0, mult = 1;
            if (array.Value.GetValue(i) is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                xp = ps.Properties.GetFloat("CurrentSkillXP_");
                mult = ps.Properties.GetFloat("CurrentXPMultiplier_", 1);
            }
            result.Add(new PlayerSkill(i, xp, mult));
        }
        return result;
    }

    private static IReadOnlyList<string> ReadNameArray(IList<FPropertyTag> root, string prefix)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            var element = array.Value.GetValue(i);
            var s = element switch
            {
                UeSaveGame.FString fs => fs.Value,
                string raw => raw,
                _ => element?.ToString(),
            };
            if (!string.IsNullOrEmpty(s)) result.Add(s!);
        }
        return result;
    }

    // ---------- internal helpers ----------

    internal static IList<FPropertyTag> GetCharacterSaveData(SaveGame save)
    {
        if (save.Properties is null || save.Properties.Count == 0)
        {
            throw new InvalidDataException("Save has no properties.");
        }

        var top = save.Properties.FindByPrefix("CharacterSaveData")
            ?? throw new InvalidDataException("Player save is missing the CharacterSaveData property.");

        if (top.Property is not StructProperty sp || sp.Value is not PropertiesStruct ps)
        {
            throw new InvalidDataException("CharacterSaveData was not a struct of properties.");
        }
        return ps.Properties;
    }

    private static CharacterStats ReadStats(IList<FPropertyTag> root)
    {
        // The wrapper field is named CurrentSurvivalStats_<hash>; its struct type is
        // CharacterStatsSave_Struct.
        //
        // Abiotic Factor delta-serializes: a stat still at its blueprint default is
        // omitted from the struct entirely (e.g. a fresh character that never ate has
        // no Hunger_ tag because hunger is still full). The blueprint default for all
        // five survival stats is 100 ("full"; Continence drains downward from 100 just
        // like the others), so a missing tag must read as 100 - not 0, which would
        // display a brand-new character as starving.
        var statsTag = root.FindByPrefix("CurrentSurvivalStats_");
        double hunger = 100, thirst = 100, sanity = 100, fatigue = 100, continence = 100;
        if (statsTag?.Property is StructProperty statsSp && statsSp.Value is PropertiesStruct statsPs)
        {
            hunger = statsPs.Properties.GetDouble("Hunger_", 100);
            thirst = statsPs.Properties.GetDouble("Thirst_", 100);
            sanity = statsPs.Properties.GetDouble("Sanity_", 100);
            fatigue = statsPs.Properties.GetDouble("Fatigue_", 100);
            continence = statsPs.Properties.GetDouble("Continence_", 100);
        }

        var money = (int)root.GetLong("CurrentMoney_", defaultValue: 0);
        return new CharacterStats(hunger, thirst, sanity, fatigue, continence, money);
    }

    private static IReadOnlyList<InventoryItemSlot> ReadInventoryArray(IList<FPropertyTag> root, string prefix)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<InventoryItemSlot>();
        }

        var slots = new List<InventoryItemSlot>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            var element = array.Value.GetValue(i);
            slots.Add(ReadSlot(i, element));
        }
        return slots;
    }

    private static InventoryItemSlot ReadSlot(int index, object? element)
    {
        // Array elements for a struct-typed array are StructProperty wrappers whose Value
        // is the IStructData payload (PropertiesStruct for unknown structs).
        if (element is not StructProperty outer || outer.Value is not PropertiesStruct ps)
        {
            return EmptySlot(index);
        }

        string? itemId = null;
        var rowHandle = ps.Properties.FindByPrefix("ItemDataTable_");
        if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
        {
            itemId = rhPs.Properties.GetString("RowName");
        }

        var changeable = ps.Properties.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs)
        {
            return new InventoryItemSlot(index, itemId, 1, 0, 0, 0, 0, null, false, null, null);
        }

        var p = cPs.Properties;
        return new InventoryItemSlot(
            Index: index,
            ItemId: itemId,
            Count: (int)p.GetLong("CurrentStack_", 1),
            Durability: p.GetDouble("CurrentItemDurability_"),
            MaxDurability: p.GetDouble("MaxItemDurability_"),
            AmmoInMagazine: (int)p.GetLong("CurrentAmmoInMagazine_"),
            LiquidLevel: (int)p.GetLong("LiquidLevel_"),
            LiquidType: p.GetEnumString("CurrentLiquid_"),
            DynamicState: p.GetBool("DynamicState_"),
            PlayerMadeString: p.GetString("PlayerMadeString_"),
            AssetId: p.GetString("AssetID_"));
    }

    private static InventoryItemSlot EmptySlot(int index)
        => new(index, null, 0, 0, 0, 0, 0, null, false, null, null);

    // ---------- carried pets ----------

    /// <summary>
    /// Scans the equipment / hotbar / main arrays for <c>Item.Pet</c> rows and reads each as a
    /// <see cref="CarriedPet"/> (durability = health, <c>DynamicProperties_</c> = XP / mutation).
    /// </summary>
    private static List<CarriedPet> ReadCarriedPets(IList<FPropertyTag> root)
    {
        var result = new List<CarriedPet>();
        ReadCarriedPetsFrom(root, "EquipmentInventory_", PetSlotKind.Equipment, result);
        ReadCarriedPetsFrom(root, "HotbarInventory_", PetSlotKind.Hotbar, result);
        ReadCarriedPetsFrom(root, "Inventory_", PetSlotKind.Main, result);
        return result;
    }

    private static void ReadCarriedPetsFrom(IList<FPropertyTag> root, string prefix, PetSlotKind kind, List<CarriedPet> result)
    {
        if (root.FindByPrefix(prefix)?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty slot || slot.Value is not PropertiesStruct sps) continue;

            string? row = null;
            if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
            {
                row = rhp.Properties.GetString("RowName");
            }
            if (!PetItemCatalog.IsPetItem(row)) continue;

            double health = 0, maxHealth = 0;
            if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
            {
                var p = cdps.Properties;
                health = p.GetDouble("CurrentItemDurability_");
                maxHealth = p.GetDouble("MaxItemDurability_");
            }
            var name = sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd2 && cd2.Value is PropertiesStruct cdp2
                ? cdp2.Properties.GetString("PlayerMadeString_")
                : null;

            result.Add(new CarriedPet(
                kind, i, row!,
                string.IsNullOrEmpty(name) ? null : name,
                health, maxHealth,
                Xp: ReadSlotDynamicInt(sps, "XP"),
                MutationProgress: ReadSlotDynamicInt(sps, "MutationProgress"),
                PetMutation: ReadSlotDynamicInt(sps, "PetMutation")));
        }
    }

    /// <summary>Reads one int from a slot's <c>ChangeableData_ -> DynamicProperties_</c> by enum tail.</summary>
    private static int ReadSlotDynamicInt(PropertiesStruct slot, string keySuffix)
    {
        if (slot.Properties.FindByPrefix("ChangeableData_")?.Property is not StructProperty cd
            || cd.Value is not PropertiesStruct cdps) return 0;
        if (cdps.Properties.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap || ap.Value is null) return 0;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty e || e.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.EndsWith("::" + keySuffix, StringComparison.Ordinal))
            {
                return eps.Properties.FindByPrefix("Value")?.Property?.Value switch { int ii => ii, long ll => (int)ll, _ => 0 };
            }
        }
        return 0;
    }

}
