using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Applies mutations to the underlying <see cref="SaveGame"/> tree of a
/// <see cref="PlayerSaveData"/>. The save then re-serializes byte-perfect except for the
/// edited fields.
/// </summary>
public static class PlayerSaveWriter
{
    /// <summary>
    /// Full hash-suffixed blueprint property names, harvested from fixture saves.
    ///
    /// Abiotic Factor delta-serializes saves: any property still at its blueprint
    /// default is omitted entirely (e.g. a fresh character has no <c>Hunger_</c> tag
    /// inside <c>CurrentSurvivalStats_</c> because hunger is still at the full 100).
    /// To write such a stat the missing <see cref="FPropertyTag"/> must be created,
    /// and that requires the exact full name - the hash suffix is part of the name the
    /// game looks up. These suffixes are emitted by the blueprint compiler and are
    /// stable across game patches (verified identical between build -2146453646 and
    /// -2146453647 saves).
    /// </summary>
    internal static class FullNames
    {
        public const string Hunger = "Hunger_2_A6C5CC6E41993323B119FA9E0B3894CA";
        public const string Thirst = "Thirst_7_E620D3DA44520EAC8EBFA28ECD77E6DA";
        public const string Sanity = "Sanity_8_1EA1DBDE4CEA799B882ABBB9EF766161";
        public const string Fatigue = "Fatigue_9_D4A267F046B9CD6F07518AAF88356DBE";
        public const string Continence = "Continence_11_29DC4A474C89E8B517691D8C627AA2F9";
        public const string CurrentMoney = "CurrentMoney_85_7425E5BF43364C11279E4C8C26F5A7CA";

        // ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 members. The game writes
        // these sparsely too (an empty transmog slot carries only AssetID_), so slot
        // edits need the same create-on-miss treatment as survival stats. Verified
        // identical across all four fixture player saves.
        public const string CurrentStack = "CurrentStack_9_D443B69044D640B0989FD8A629801A49";
        public const string CurrentItemDurability = "CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8";
        public const string MaxItemDurability = "MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B";
        public const string CurrentAmmoInMagazine = "CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D";
        public const string LiquidLevel = "LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A";
        public const string DynamicState = "DynamicState_39_7597AC6549E292B931C61BB13C9E42EB";
        public const string PlayerMadeString = "PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9";
        public const string AssetId = "AssetID_25_06DB7A12469849D19D5FC3BA6BEDEEAB";
    }

    private static string ArrayPrefixFor(PetSlotKind kind) => kind switch
    {
        PetSlotKind.Equipment => "EquipmentInventory_",
        PetSlotKind.Hotbar => "HotbarInventory_",
        _ => "Inventory_",
    };

    private static ArrayProperty? GetInventoryArray(IList<FPropertyTag> root, PetSlotKind kind)
        => root.FindByPrefix(ArrayPrefixFor(kind))?.Property as ArrayProperty;

    private static bool SlotIsEmpty(PropertiesStruct slot)
    {
        if (slot.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            var row = rhp.Properties.GetString("RowName");
            return string.IsNullOrEmpty(row) || row is "None" or "Empty";
        }
        return true;
    }

    /// <summary>First empty slot index in a player inventory array, or -1 when full / absent.</summary>
    public static int FindFreeSlot(PlayerSaveData data, PetSlotKind kind)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return -1;
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr.GetValue(i) is StructProperty sp && sp.Value is PropertiesStruct ps && SlotIsEmpty(ps)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Patches an existing carried pet in place: name, health (durability), and XP / mutation.
    /// The slot must already hold a pet item (its DynamicProperties supply the element template).
    /// </summary>
    public static void ApplyCarriedPet(PlayerSaveData data, CarriedPet pet)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, pet.Slot) is not { Value: { } arr }) return;
        if (pet.Index < 0 || pet.Index >= arr.Length) return;
        if (arr.GetValue(pet.Index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", pet.ItemRow);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            var p = cdps.Properties;
            SetDouble(p, "CurrentItemDurability_", pet.Health, FullNames.CurrentItemDurability);
            SetDouble(p, "MaxItemDurability_", pet.MaxHealth, FullNames.MaxItemDurability);
            SetString(p, "PlayerMadeString_", pet.Name, pet.Name is null ? null : FullNames.PlayerMadeString);
            PetDynamicProperties.SetOrAdd(p, "XP", pet.Xp);
            PetDynamicProperties.SetOrAdd(p, "MutationProgress", pet.MutationProgress);
            PetDynamicProperties.SetOrAdd(p, "PetMutation", pet.PetMutation);
        }
    }

    /// <summary>
    /// Places a pet item into a free slot of <paramref name="kind"/> (or the given
    /// <paramref name="index"/>), building full pet data (health, XP, mutation). The
    /// DynamicProperties template is found anywhere in the save; when none exists the pet is
    /// written with variant + health only. Returns the slot index used, or -1 when no free slot.
    /// </summary>
    public static int AddCarriedPetToSlot(PlayerSaveData data, PetSlotKind kind, int index, CarriedPet pet)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return -1;

        if (index < 0)
        {
            index = FindFreeSlot(data, kind);
            if (index < 0) return -1;
        }
        if (index >= arr.Length) return -1;
        if (arr.GetValue(index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return -1;
        if (!SlotIsEmpty(sps)) return -1;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", pet.ItemRow);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            var p = cdps.Properties;
            SetInt(p, "CurrentStack_", 1, FullNames.CurrentStack);
            SetDouble(p, "CurrentItemDurability_", pet.Health, FullNames.CurrentItemDurability);
            SetDouble(p, "MaxItemDurability_", pet.MaxHealth, FullNames.MaxItemDurability);
            SetBool(p, "DynamicState_", true, FullNames.DynamicState);
            SetString(p, "AssetID_", Guid.NewGuid().ToString("N").ToUpperInvariant(), FullNames.AssetId);
            if (pet.Name is not null) SetString(p, "PlayerMadeString_", pet.Name, FullNames.PlayerMadeString);

            var template = PetDynamicProperties.CaptureTemplate(data.Raw);
            PetDynamicProperties.WriteArray(p, template, new[]
            {
                ("EDynamicProperty::XP", pet.Xp),
                ("EDynamicProperty::MutationProgress", pet.MutationProgress),
                ("EDynamicProperty::PetMutation", pet.PetMutation),
            });
        }
        return index;
    }

    /// <summary>Clears a carried pet's slot back to empty. Returns true when a pet was there.</summary>
    public static bool RemoveCarriedPet(PlayerSaveData data, PetSlotKind kind, int index)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (GetInventoryArray(root, kind) is not { Value: { } arr }) return false;
        if (index < 0 || index >= arr.Length) return false;
        if (arr.GetValue(index) is not StructProperty slot || slot.Value is not PropertiesStruct sps) return false;
        if (SlotIsEmpty(sps)) return false;

        if (sps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rh && rh.Value is PropertiesStruct rhp)
        {
            SetName(rhp.Properties, "RowName", EmptySlotRowName);
        }
        if (sps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cd && cd.Value is PropertiesStruct cdps)
        {
            SetInt(cdps.Properties, "CurrentStack_", 0, FullNames.CurrentStack);
        }
        return true;
    }

    /// <summary>
    /// Patches the stats sub-struct in <paramref name="data"/>'s raw save tree to reflect
    /// <paramref name="newStats"/>. Stats the save omitted (delta-serialization of
    /// default-valued properties) get a freshly created tag. Does not write to disk;
    /// call <see cref="WriteToFile(PlayerSaveData, string)"/> for that.
    /// </summary>
    public static void ApplyStats(PlayerSaveData data, CharacterStats newStats)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);

        var statsTag = root.FindByPrefix("CurrentSurvivalStats_");
        if (statsTag?.Property is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            SetDouble(ps.Properties, "Hunger_", newStats.Hunger, FullNames.Hunger);
            SetDouble(ps.Properties, "Thirst_", newStats.Thirst, FullNames.Thirst);
            SetDouble(ps.Properties, "Sanity_", newStats.Sanity, FullNames.Sanity);
            SetDouble(ps.Properties, "Fatigue_", newStats.Fatigue, FullNames.Fatigue);
            SetDouble(ps.Properties, "Continence_", newStats.Continence, FullNames.Continence);
        }

        SetInt(root, "CurrentMoney_", newStats.Money, FullNames.CurrentMoney);
    }

    /// <summary>
    /// Patches the three inventory arrays in <paramref name="data"/>'s raw save tree to
    /// reflect <paramref name="updated"/>. Each slot in <paramref name="updated"/> is
    /// matched to the raw tree by array index; the writer walks the corresponding
    /// <c>ChangeableData</c> struct and updates CurrentStack / Durability / Ammo /
    /// LiquidLevel / DynamicState / PlayerMadeString. Item ID (RowName) is also patched.
    /// </summary>
    public static void ApplyInventory(PlayerSaveData data, PlayerInventory updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);

        ApplyInventoryArray(root, "EquipmentInventory_", updated.Equipment);
        ApplyInventoryArray(root, "HotbarInventory_", updated.Hotbar);
        ApplyInventoryArray(root, "Inventory_", updated.Main);
    }

    /// <summary>
    /// Patches the 6 <c>TransmogInventory_</c> slots from <paramref name="updated"/>,
    /// matched by array index - same in-place patching as the other inventory arrays.
    /// Saves without a transmog array (older game versions) are skipped silently.
    /// </summary>
    public static void ApplyTransmogSlots(PlayerSaveData data, IReadOnlyList<InventoryItemSlot> updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ApplyInventoryArray(root, "TransmogInventory_", updated);
    }

    /// <summary>
    /// Patches the 12 <c>TransmogVisibility_</c> bool flags in place, matched by index.
    /// Indices beyond the existing array length are skipped - the array is never resized.
    /// Saves without the property are skipped silently.
    /// </summary>
    public static void ApplyTransmogVisibility(PlayerSaveData data, IReadOnlyList<bool> visibility)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("TransmogVisibility_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < visibility.Count; i++)
        {
            array.Value.SetValue(visibility[i], i);
        }
    }

    /// <summary>
    /// The row name Abiotic Factor writes into an empty inventory slot's
    /// <c>ItemDataTable_.RowName</c> (see <see cref="InventoryItemSlot.IsEmpty"/>).
    /// </summary>
    public const string EmptySlotRowName = "Empty";

    /// <summary>
    /// Clears every slot of the equipment / hotbar / main / transmog arrays to the empty
    /// sentinel (<see cref="EmptySlotRowName"/>, stack 0), leaving the array structure
    /// intact. Used when fabricating a fresh (blank) player from an existing save's shape.
    /// </summary>
    public static void ClearAllInventory(PlayerSaveData data)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ClearInventoryArray(root, "EquipmentInventory_");
        ClearInventoryArray(root, "HotbarInventory_");
        ClearInventoryArray(root, "Inventory_");
        ClearInventoryArray(root, "TransmogInventory_");
    }

    private static void ClearInventoryArray(IList<FPropertyTag> root, string prefix)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            if (ps.Properties.FindByPrefix("ItemDataTable_")?.Property is StructProperty rhSp
                && rhSp.Value is PropertiesStruct rhPs)
            {
                SetName(rhPs.Properties, "RowName", EmptySlotRowName);
            }

            if (ps.Properties.FindByPrefix("ChangeableData_")?.Property is StructProperty cSp
                && cSp.Value is PropertiesStruct cPs)
            {
                SetInt(cPs.Properties, "CurrentStack_", 0, FullNames.CurrentStack);
            }
        }
    }

    /// <summary>
    /// Patches the respawn pair: <c>LastSafeWorldLocation_</c> (Vector) and - when
    /// <paramref name="levelGuid"/> is non-null - <c>LastSafeWorldGUID_</c>. The
    /// terminal id is an engine-level actor reference and is left untouched.
    /// </summary>
    public static void ApplyRespawn(PlayerSaveData data, double x, double y, double z, string? levelGuid = null)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);

        if (root.FindByPrefix("LastSafeWorldLocation_")?.Property is StructProperty sp
            && sp.Value is VectorStruct vec)
        {
            vec.Value = new UeSaveGame.DataTypes.FVector { X = x, Y = y, Z = z };
        }

        if (!string.IsNullOrEmpty(levelGuid))
        {
            SetString(root, "LastSafeWorldGUID_", levelGuid);
        }
    }

    /// <summary>
    /// Patches <c>TerminalRespawnID_</c> (NameProperty) - the static punch-card terminal
    /// the player respawns at. See <see cref="RespawnTerminalCatalog"/> for valid values.
    /// </summary>
    public static void ApplyRespawnTerminal(PlayerSaveData data, string terminalGuid)
    {
        if (string.IsNullOrEmpty(terminalGuid)) return;
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        SetName(root, "TerminalRespawnID_", terminalGuid);
    }

    private static void ApplyInventoryArray(IList<FPropertyTag> root, string prefix, IReadOnlyList<InventoryItemSlot> updated)
    {
        var tag = root.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < updated.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            var newSlot = updated[i];
            ApplySlot(ps.Properties, newSlot);
        }
    }

    private static void ApplySlot(IList<FPropertyTag> slotProps, InventoryItemSlot newSlot)
    {
        // RowName (item ID) - only patch if a valid id is provided; clearing to None
        // would require knowing the empty-slot sentinel AF uses, which differs.
        if (!string.IsNullOrEmpty(newSlot.ItemId))
        {
            var rowHandle = slotProps.FindByPrefix("ItemDataTable_");
            if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
            {
                SetName(rhPs.Properties, "RowName", newSlot.ItemId);
            }
        }

        var changeable = slotProps.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs) return;

        // ChangeableData is delta-serialized like everything else - a slot the game
        // wrote sparsely (e.g. an empty transmog slot has only AssetID_) is missing the
        // numeric/string tags entirely, so each edit creates the tag when absent.
        var p = cPs.Properties;
        SetInt(p, "CurrentStack_", newSlot.Count, FullNames.CurrentStack);
        SetDouble(p, "CurrentItemDurability_", newSlot.Durability, FullNames.CurrentItemDurability);
        SetDouble(p, "MaxItemDurability_", newSlot.MaxDurability, FullNames.MaxItemDurability);
        SetInt(p, "CurrentAmmoInMagazine_", newSlot.AmmoInMagazine, FullNames.CurrentAmmoInMagazine);
        SetInt(p, "LiquidLevel_", newSlot.LiquidLevel, FullNames.LiquidLevel);
        SetBool(p, "DynamicState_", newSlot.DynamicState, FullNames.DynamicState);
        // Null means "no player text" - never create a tag just to hold null.
        SetString(p, "PlayerMadeString_", newSlot.PlayerMadeString,
            newSlot.PlayerMadeString is null ? null : FullNames.PlayerMadeString);
        // AssetID is the per-instance GUID the game tracks items by. A freshly added item
        // carries a new id (see SlotSwap.FillFromCatalog); write it create-on-miss so the
        // game registers and renders the item. Null means "leave the slot's existing id".
        SetString(p, "AssetID_", newSlot.AssetId,
            newSlot.AssetId is null ? null : FullNames.AssetId);
    }

    /// <summary>
    /// Patches the positional <c>Skills_</c> array from <paramref name="updated"/>.
    /// Skills are matched by array index; only <c>CurrentSkillXP_</c> and
    /// <c>CurrentXPMultiplier_</c> are written (the SkillName text fields are inert
    /// blueprint defaults the game ignores). Out-of-range entries are skipped.
    /// </summary>
    public static void ApplySkills(PlayerSaveData data, IReadOnlyList<PlayerSkill> updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("Skills_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        foreach (var skill in updated)
        {
            if (skill.Index < 0 || skill.Index >= array.Value.Length) continue;
            if (array.Value.GetValue(skill.Index) is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            SetFloat(ps.Properties, "CurrentSkillXP_", skill.Xp);
            SetFloat(ps.Properties, "CurrentXPMultiplier_", skill.XpMultiplier);
        }
    }

    /// <summary>
    /// Replaces the <c>Traits_</c> name array with <paramref name="traits"/> (internal
    /// row names like <c>Trait_LeadBelly</c>). Mirrors the WorldFlags writer: the existing
    /// ArrayProperty instance is kept, only its element buffer is swapped.
    /// </summary>
    public static void ApplyTraits(PlayerSaveData data, IReadOnlyList<string> traits)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("Traits_");
        if (tag?.Property is not ArrayProperty array) return;

        var items = new FString[traits.Count];
        for (var i = 0; i < traits.Count; i++)
        {
            items[i] = new FString(traits[i]);
        }
        array.Value = items;
    }

    /// <summary>Sets the <c>PhD_</c> background row name (e.g. <c>PhD_HumanBio</c>).</summary>
    public static void ApplyPhd(PlayerSaveData data, string phd)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        SetName(root, "PhD_", phd);
    }

    /// <summary>Patches the six limb values of <c>CharacterHealth_</c>.</summary>
    public static void ApplyLimbHealth(PlayerSaveData data, LimbHealth health)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("CharacterHealth_");
        if (tag?.Property is not StructProperty sp || sp.Value is not PropertiesStruct ps) return;

        var p = ps.Properties;
        SetDouble(p, "Head_", health.Head);
        SetDouble(p, "Torso_", health.Torso);
        SetDouble(p, "LeftArm_", health.LeftArm);
        SetDouble(p, "RightArm_", health.RightArm);
        SetDouble(p, "LeftLeg_", health.LeftLeg);
        SetDouble(p, "RightLeg_", health.RightLeg);
    }

    /// <summary>
    /// Replaces the <c>RecipesUnlock_</c> name array (recipe row names like
    /// <c>recipe_bandage</c>). Same swap-the-buffer pattern as traits/flags.
    /// </summary>
    public static void ApplyRecipes(PlayerSaveData data, IReadOnlyList<string> recipes)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "RecipesUnlock_", recipes);
    }

    /// <summary>Replaces the <c>EmailsRead_</c> name array.</summary>
    public static void ApplyEmailsRead(PlayerSaveData data, IReadOnlyList<string> emails)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "EmailsRead_", emails);
    }

    /// <summary>Replaces the <c>JournalEntries_</c> name array.</summary>
    public static void ApplyJournals(PlayerSaveData data, IReadOnlyList<string> journals)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "JournalEntries_", journals);
    }

    /// <summary>
    /// Replaces the three compendium section arrays. An entry counts as unlocked when
    /// its row name is present in the array matching each of its sections' unlock types.
    /// </summary>
    public static void ApplyCompendium(
        PlayerSaveData data,
        IReadOnlyList<string> emailSections,
        IReadOnlyList<string> narrativeSections,
        IReadOnlyList<string> explorationSections)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "Compendium_EmailSections_", emailSections);
        ReplaceNameArray(root, "Compendium_NarrativeSections_", narrativeSections);
        ReplaceNameArray(root, "Compendium_ExplorationSections_", explorationSections);
    }

    /// <summary>Replaces the <c>ItemsPickedUp_</c> name array (item row names).</summary>
    public static void ApplyItemsPickedUp(PlayerSaveData data, IReadOnlyList<string> items)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "ItemsPickedUp_", items);
    }

    /// <summary>Replaces the <c>CraftedItems_</c> name array (item row names).</summary>
    public static void ApplyCraftedItems(PlayerSaveData data, IReadOnlyList<string> items)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "CraftedItems_", items);
    }

    /// <summary>Replaces the <c>MapsUnlocked_</c> name array (DT_MapPamphlets rows).</summary>
    public static void ApplyMapsUnlocked(PlayerSaveData data, IReadOnlyList<string> maps)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "MapsUnlocked_", maps);
    }

    /// <summary>
    /// Patches the <c>Count</c> of existing <c>Compendium_KillCount_</c> entries, matched
    /// by their <c>CompendiumRow.RowName</c>. Entries the save doesn't carry yet are
    /// skipped - the array only grows when the game records a first kill.
    /// </summary>
    public static void ApplyKillCounts(PlayerSaveData data, IReadOnlyList<KillCount> updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("Compendium_KillCount_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        var byRow = updated.ToDictionary(k => k.CompendiumRow, k => k.Count, StringComparer.Ordinal);
        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            string? row = null;
            if (ps.Properties.FindByPrefix("CompendiumRow")?.Property is StructProperty rowSp
                && rowSp.Value is PropertiesStruct rowPs)
            {
                row = rowPs.Properties.FirstOrDefault(p2 => p2.Name?.Value == "RowName")?.Property?.Value?.ToString();
            }
            if (row is not null && byRow.TryGetValue(row, out var count))
            {
                SetInt(ps.Properties, "Count", count);
            }
        }
    }

    /// <summary>Replaces the <c>Compendium_Fish_</c> name array (DT_Fish rows).</summary>
    public static void ApplyFishCaught(PlayerSaveData data, IReadOnlyList<string> fish)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "Compendium_Fish_", fish);
    }

    private static void ReplaceNameArray(IList<FPropertyTag> tags, string prefix, IReadOnlyList<string> values)
    {
        var tag = tags.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array) return;

        var items = new FString[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            items[i] = new FString(values[i]);
        }
        array.Value = items;
    }

    /// <summary>
    /// Writes <paramref name="data"/>'s raw save to disk. The previous file content is
    /// preserved as <c>&lt;path&gt;.bak</c> so one bad write can't destroy a save.
    /// </summary>
    public static void WriteToFile(PlayerSaveData data, string path)
    {
        Diagnostics.EditorLog.Info("PlayerSave", $"Writing {path} (previous content kept as {Path.GetFileName(path)}.bak)");
        try
        {
            Saves.SaveBackup.WriteWithBackup(path, data.Raw.WriteTo);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("PlayerSave", $"Failed to write {path}", ex);
            throw;
        }
    }

    /// <summary>
    /// Finds the property matching <paramref name="prefix"/>. When absent and
    /// <paramref name="createFullName"/> is given, a new <see cref="FPropertyTag"/> of
    /// <paramref name="typeName"/> is created and appended (the trailing <c>None</c>
    /// terminator is emitted by the serializer, so appending is safe).
    ///
    /// Why creation is needed: Abiotic Factor delta-serializes - properties at their
    /// blueprint default are omitted from the file, so a prefix lookup can legitimately
    /// fail on a healthy save (see <see cref="FullNames"/>). Without creation the edit
    /// would silently no-op. New tags use <see cref="EPropertyTagFlags.None"/>, matching
    /// every game-written primitive tag observed in fixture saves.
    /// </summary>
    private static FProperty? FindOrCreate(IList<FPropertyTag> tags, string prefix, string? createFullName, string typeName)
    {
        var existing = tags.FindByPrefix(prefix)?.Property;
        if (existing is not null || createFullName is null)
        {
            return existing;
        }

        var name = new FString(createFullName);
        var type = new FPropertyTypeName(name: new FString(typeName));
        var property = FProperty.Create(name, type);
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
        return property;
    }

    private static void SetFloat(IList<FPropertyTag> tags, string prefix, float value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(FloatProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(DoubleProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(IntProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(BoolProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(StrProperty));
        if (p is not null)
        {
            // StrProperty stores null differently than empty string; preserve null.
            p.Value = value is null ? null : (object)new FString(value);
        }
    }

    private static void SetName(IList<FPropertyTag> tags, string prefix, string value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(NameProperty));
        if (p is not null)
        {
            // NameProperty stores FString values.
            p.Value = new FString(value);
        }
    }
}
