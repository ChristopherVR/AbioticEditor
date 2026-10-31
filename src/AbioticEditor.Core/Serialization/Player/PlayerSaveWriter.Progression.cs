using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - discovery/progression lists: recipes, codex, kills, fish, maps.
public static partial class PlayerSaveWriter
{
    /// <summary>
    /// Replaces the <c>RecipesUnlock_</c> name array (recipe row names like
    /// <c>recipe_bandage</c>). Same swap-the-buffer pattern as traits/flags.
    ///
    /// When <paramref name="syncNewBadge"/> is true (default - matches what the game itself
    /// does on discovery), a recipe that becomes newly unlocked is also added to
    /// <c>NewestRecipes_</c> so the in-game "NEW" toast still fires, and a recipe that gets
    /// relocked is removed from it so the badge doesn't linger on content the player no
    /// longer has.
    /// </summary>
    public static void ApplyRecipes(PlayerSaveData data, IReadOnlyList<string> recipes, bool syncNewBadge = true)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (syncNewBadge) SyncNewBadge(root, "RecipesUnlock_", recipes, "NewestRecipes_", FullNames.NewestRecipes);
        ReplaceNameArray(root, "RecipesUnlock_", recipes, CreateWhenFilled(recipes, FullNames.RecipesUnlock));
    }

    /// <summary>Replaces the <c>EmailsRead_</c> name array.</summary>
    public static void ApplyEmailsRead(PlayerSaveData data, IReadOnlyList<string> emails)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "EmailsRead_", emails, CreateWhenFilled(emails, FullNames.EmailsRead));
    }

    /// <summary>
    /// Replaces the <c>JournalEntries_</c> name array. <paramref name="syncNewBadge"/> keeps
    /// <c>Journal_Unread_</c> in sync the same way <see cref="ApplyRecipes"/> keeps
    /// <c>NewestRecipes_</c> in sync.
    /// </summary>
    public static void ApplyJournals(PlayerSaveData data, IReadOnlyList<string> journals, bool syncNewBadge = true)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (syncNewBadge) SyncNewBadge(root, "JournalEntries_", journals, "Journal_Unread_", FullNames.JournalUnread);
        ReplaceNameArray(root, "JournalEntries_", journals, CreateWhenFilled(journals, FullNames.JournalEntries));
    }

    /// <summary>
    /// Replaces the three compendium section arrays. An entry counts as unlocked when
    /// its row name is present in the array matching each of its sections' unlock types.
    /// <paramref name="syncNewBadge"/> keeps <c>Compendium_Unread_</c> in sync against the
    /// combined (email + narrative + exploration) set, the same way <see cref="ApplyRecipes"/>
    /// keeps <c>NewestRecipes_</c> in sync.
    /// </summary>
    public static void ApplyCompendium(
        PlayerSaveData data,
        IReadOnlyList<string> emailSections,
        IReadOnlyList<string> narrativeSections,
        IReadOnlyList<string> explorationSections,
        bool syncNewBadge = true)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (syncNewBadge)
        {
            var combined = emailSections.Concat(narrativeSections).Concat(explorationSections)
                .Distinct(StringComparer.Ordinal).ToList();
            var before = GvasTags.ReadNameArray(root, "Compendium_EmailSections_")
                .Concat(GvasTags.ReadNameArray(root, "Compendium_NarrativeSections_"))
                .Concat(GvasTags.ReadNameArray(root, "Compendium_ExplorationSections_"))
                .Distinct(StringComparer.Ordinal).ToList();
            SyncNewBadgeFromSnapshot(root, before, combined, "Compendium_Unread_", FullNames.CompendiumUnread);
        }
        ReplaceNameArray(root, "Compendium_EmailSections_", emailSections, CreateWhenFilled(emailSections, FullNames.CompendiumEmailSections));
        ReplaceNameArray(root, "Compendium_NarrativeSections_", narrativeSections, CreateWhenFilled(narrativeSections, FullNames.CompendiumNarrativeSections));
        ReplaceNameArray(root, "Compendium_ExplorationSections_", explorationSections, CreateWhenFilled(explorationSections, FullNames.CompendiumExplorationSections));
    }

    /// <summary>Replaces the <c>ItemsPickedUp_</c> name array (item row names).</summary>
    public static void ApplyItemsPickedUp(PlayerSaveData data, IReadOnlyList<string> items)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "ItemsPickedUp_", items, CreateWhenFilled(items, FullNames.ItemsPickedUp));
    }

    /// <summary>Replaces the <c>CraftedItems_</c> name array (item row names).</summary>
    public static void ApplyCraftedItems(PlayerSaveData data, IReadOnlyList<string> items)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "CraftedItems_", items, CreateWhenFilled(items, FullNames.CraftedItems));
    }

    /// <summary>
    /// Replaces the <c>ItemsDistilled_</c> array (lower-case item row names the distillery has
    /// seen). Created from its full name when the character has never distilled anything,
    /// because the game omits an empty array.
    /// </summary>
    public static void ApplyItemsDistilled(PlayerSaveData data, IReadOnlyList<string> items)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "ItemsDistilled_", items,
            CreateWhenFilled(items, FullNames.ItemsDistilled));
    }

    /// <summary>
    /// Removes active effects (<c>CurrentBuffDebuffs_</c>) so only <paramref name="keep"/> remain.
    /// The entries that stay are the game's own untouched structs. When nothing is kept the whole
    /// struct is dropped, which is how the game stores a character with no effects (it
    /// delta-serializes an empty list away). Entries in <paramref name="keep"/> that the save
    /// does not have are ignored here; <see cref="AddActiveBuffs"/> adds those.
    /// </summary>
    public static bool ApplyActiveBuffs(PlayerSaveData data, IReadOnlyList<ActiveBuff> keep)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("ActiveBuffs") ?? root.FindByPrefix("CurrentBuffDebuffs_");
        var array = tag?.Property as ArrayProperty;
        if (array is null && tag?.Property is StructProperty { Value: PropertiesStruct ps })
            array = ps.Properties.FindByPrefix("Buffs_")?.Property as ArrayProperty;
        if (tag is null || array?.Value is null)
        {
            return false;
        }

        var remaining = keep.ToList();
        var kept = new List<object>();
        for (var i = 0; i < array.Value.Length; i++)
        {
            var element = array.Value.GetValue(i);
            if (element is not StructProperty esp || esp.Value is not PropertiesStruct eps) { kept.Add(element!); continue; }
            var row = eps.Properties.FindByPrefix("BuffRow")?.Property is StructProperty rowSp && rowSp.Value is PropertiesStruct rowPs
                ? rowPs.Properties.GetString("RowName") : null;
            var entry = new ActiveBuff(row ?? string.Empty, eps.Properties.GetEnumString("ParentLimb"), eps.Properties.GetFloat("BuffExpireTime"));
            var match = remaining.FindIndex(b => b == entry);
            if (match < 0) continue;
            remaining.RemoveAt(match);
            kept.Add(element!);
        }
        if (kept.Count == array.Value.Length) return false;

        if (kept.Count == 0)
        {
            root.Remove(tag);
            return true;
        }
        var rebuilt = Array.CreateInstance(array.Value.GetType().GetElementType()!, kept.Count);
        for (var i = 0; i < kept.Count; i++) rebuilt.SetValue(kept[i], i);
        array.Value = rebuilt;
        return true;
    }

    /// <summary>
    /// Appends active effects to <c>CurrentBuffDebuffs_</c>, creating the list when the
    /// character has none. <see cref="ActiveBuff.ExpireTime"/> is what the game reads back as the
    /// effect's duration when the save loads: <c>Apply Save Data to Character</c> passes every
    /// saved entry to <c>Server_ApplyBuff(BuffRow, bOverrideDefaultDuration: true,
    /// NewDuration: BuffExpireTime, Limb: ParentLimb)</c>, so a number of seconds lasts that long
    /// and -1 is how permanent effects are stored. A null <see cref="ActiveBuff.ParentLimb"/>
    /// becomes <c>EBodyLimbs::AllBones</c>, the limb every saved entry in the fixtures carries.
    /// A missing list is created in the save's own layout: older saves wrap it in
    /// <c>BuffSave_Struct</c>, native 1.5 saves keep a plain <c>ActiveBuffs</c> list of the same
    /// <c>BuffDebuffEntry</c> struct (from the game's usmap: <c>AbioticCharacterSaveData.ActiveBuffs</c>).
    /// Returns false (and changes nothing) when there is nothing to add.
    /// </summary>
    public static bool AddActiveBuffs(PlayerSaveData data, IReadOnlyList<ActiveBuff> add)
    {
        var toAdd = add.Where(b => !string.IsNullOrWhiteSpace(b.BuffRow)).ToList();
        if (toAdd.Count == 0) return false;
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("ActiveBuffs") ?? root.FindByPrefix("CurrentBuffDebuffs_");
        var listTag = tag?.Property is ArrayProperty ? tag : null;
        if (listTag is null && tag?.Property is StructProperty { Value: PropertiesStruct ps })
            listTag = ps.Properties.FindByPrefix("Buffs_");
        var array = listTag?.Property as ArrayProperty;
        var arrayName = listTag?.Name;
        if (array is null || arrayName is null)
        {
            if (tag is not null) return false;
            (arrayName, array) = NativeSaveSchema.IsNative(root) ? CreateNativeBuffList(root) : CreateLegacyBuffList(root);
        }

        var items = new List<FProperty>(array.Value?.Cast<FProperty>() ?? []);
        foreach (var buff in toAdd) items.Add(NewBuffEntry(arrayName, buff));
        array.Value = items.ToArray();
        return true;
    }

    private static readonly FPropertyTypeName BuffEntryStruct =
        new(new FString("BuffDebuffEntry"), [new(new FString("/Script/AbioticFactor"))]);

    private static (FString Name, ArrayProperty Array) CreateNativeBuffList(IList<FPropertyTag> root)
    {
        var element = new FPropertyTypeName(new FString(nameof(StructProperty)), [BuffEntryStruct]);
        var name = new FString("ActiveBuffs");
        var array = new ArrayProperty(name, element) { Value = Array.Empty<FProperty>() };
        root.Add(new FPropertyTag(name, new FPropertyTypeName(new FString(nameof(ArrayProperty)), [element]), EPropertyTagFlags.None) { Property = array });
        return (name, array);
    }

    // Verbatim from the Cascade fixtures: the BuffSave_Struct wrapper, its GUID and its array.
    private static (FString Name, ArrayProperty Array) CreateLegacyBuffList(IList<FPropertyTag> root)
    {
        var saveStruct = new FPropertyTypeName(new FString("BuffSave_Struct"), [new(new FString("/Game/Blueprints/Data/BuffSave_Struct"))]);
        var guid = Guid.Parse("3e5e56f6-4c57-5051-a9a6-8db56aae58a8");
        var element = new FPropertyTypeName(new FString(nameof(StructProperty)), [BuffEntryStruct]);
        var arrayName = new FString(FullNames.Buffs);
        var array = new ArrayProperty(arrayName, element) { Value = Array.Empty<FProperty>() };
        var body = new PropertiesStruct { Properties = new List<FPropertyTag>
        {
            new(arrayName, new FPropertyTypeName(new FString(nameof(ArrayProperty)), [element]), EPropertyTagFlags.None) { Property = array },
        } };
        var name = new FString(FullNames.CurrentBuffDebuffs);
        var wrapper = new StructProperty(name) { StructType = saveStruct, StructGuid = guid, Value = body };
        var tagType = new FPropertyTypeName(new FString(nameof(StructProperty)), [saveStruct, new(new FString(guid.ToString()))]);
        root.Add(new FPropertyTag(name, tagType, EPropertyTagFlags.None) { Property = wrapper });
        return (arrayName, array);
    }

    private static StructProperty NewBuffEntry(FString arrayName, ActiveBuff buff)
    {
        var module = new FPropertyTypeName(new FString("/Script/AbioticFactor"));
        var handleType = new FPropertyTypeName(new FString("BuffDebuffRowHandle"), [module]);
        var handleBody = new PropertiesStruct { Properties = new List<FPropertyTag>() };
        SetName(handleBody.Properties, "RowName", buff.BuffRow, "RowName");
        var handleName = new FString("BuffRow");
        var handle = new StructProperty(handleName) { StructType = handleType, StructGuid = Guid.Empty, Value = handleBody };

        var enumType = new FPropertyTypeName(new FString("EBodyLimbs"), [module]);
        var limbName = new FString("ParentLimb");
        var limb = new EnumProperty(limbName, enumType) { Value = new FString(buff.ParentLimb ?? "EBodyLimbs::AllBones") };
        var limbTagType = new FPropertyTypeName(new FString(nameof(EnumProperty)), [enumType, new(new FString(nameof(ByteProperty)))]);

        var body = new PropertiesStruct { Properties = new List<FPropertyTag>
        {
            new(handleName, new FPropertyTypeName(new FString(nameof(StructProperty)), [handleType]), EPropertyTagFlags.None) { Property = handle },
            new(limbName, limbTagType, EPropertyTagFlags.None) { Property = limb },
        } };
        SetFloat(body.Properties, "BuffExpireTime", buff.ExpireTime, "BuffExpireTime");
        return new StructProperty(arrayName) { StructType = BuffEntryStruct, StructGuid = Guid.Empty, Value = body };
    }

    /// <summary>Replaces the <c>MapsUnlocked_</c> name array (DT_MapPamphlets rows).</summary>
    public static void ApplyMapsUnlocked(PlayerSaveData data, IReadOnlyList<string> maps)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "MapsUnlocked_", maps, CreateWhenFilled(maps, FullNames.MapsUnlocked));
    }

    /// <summary>
    /// The full name to create a missing list under, only when there is something to put in it:
    /// the game omits an empty list, so creating one for nothing would change an untouched save.
    /// </summary>
    private static string? CreateWhenFilled(IReadOnlyList<string> values, string fullName)
        => values.Count > 0 ? fullName : null;

    /// <summary>
    /// Sets kill tallies in <c>Compendium_KillCount_</c>, matched by <c>CompendiumRow.RowName</c>.
    /// Existing entries are updated in place; a creature with no entry yet gets one appended
    /// when its count is above zero, and the list itself is created when the save has none.
    /// Every save version stores the entries as the native <c>CompendiumKillCount</c> struct.
    /// </summary>
    public static void ApplyKillCounts(PlayerSaveData data, IReadOnlyList<KillCount> updated)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var tag = root.FindByPrefix("Compendium_KillCount_");
        var array = tag?.Property as ArrayProperty;

        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kill in updated) pending[kill.CompendiumRow] = kill.Count;
        if (array?.Value is { } existing)
        {
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing.GetValue(i) is not StructProperty { Value: PropertiesStruct ps }) continue;
                var row = (ps.Properties.FindByPrefix("CompendiumRow")?.Property as StructProperty)?.Value is PropertiesStruct rowPs
                    ? rowPs.Properties.GetString("RowName") : null;
                if (row is null || !pending.Remove(row, out var count)) continue;
                if (ps.Properties.GetLong("Count") != count) SetInt(ps.Properties, "Count", count, "Count");
            }
        }

        var added = pending.Where(p => p.Value > 0).ToList();
        if (added.Count == 0) return;
        if (array is null) (tag, array) = CreateKillCountArray(root);
        var items = new List<FProperty>(array.Value?.Cast<FProperty>() ?? []);
        foreach (var (row, count) in added) items.Add(NewKillCount(tag!.Name, row, count));
        array.Value = items.ToArray();
    }

    private static readonly FPropertyTypeName KillCountStruct =
        new(new FString("CompendiumKillCount"), [new(new FString("/Script/AbioticFactor"))]);

    private static (FPropertyTag Tag, ArrayProperty Array) CreateKillCountArray(IList<FPropertyTag> root)
    {
        var owner = NativeSaveSchema.ResolveOwner(root, "Compendium_KillCount_", create: true);
        var name = new FString(NativeSaveSchema.IsNative(owner)
            ? NativeSaveSchema.Name(owner, "Compendium_KillCount_")
            : FullNames.CompendiumKillCount);
        var element = new FPropertyTypeName(new FString(nameof(StructProperty)), [KillCountStruct]);
        var array = new ArrayProperty(name, element) { Value = Array.Empty<FProperty>() };
        var tag = new FPropertyTag(name, new FPropertyTypeName(new FString(nameof(ArrayProperty)), [element]), EPropertyTagFlags.None) { Property = array };
        owner.Add(tag);
        return (tag, array);
    }

    private static StructProperty NewKillCount(FString arrayName, string row, int count)
    {
        var handleType = new FPropertyTypeName(new FString("CompendiumEntryRowHandle"), [new(new FString("/Script/AbioticFactor"))]);
        var handleBody = new PropertiesStruct { Properties = new List<FPropertyTag>() };
        SetName(handleBody.Properties, "RowName", row, "RowName");
        var handleName = new FString("CompendiumRow");
        var handle = new StructProperty(handleName) { StructType = handleType, StructGuid = Guid.Empty, Value = handleBody };

        var body = new PropertiesStruct { Properties = new List<FPropertyTag>() };
        body.Properties.Add(new FPropertyTag(handleName, new FPropertyTypeName(new FString(nameof(StructProperty)), [handleType]), EPropertyTagFlags.None) { Property = handle });
        SetInt(body.Properties, "Count", count, "Count");
        return new StructProperty(arrayName) { StructType = KillCountStruct, StructGuid = Guid.Empty, Value = body };
    }

    /// <summary>
    /// Replaces the <c>Compendium_Fish_</c> name array (DT_Fish rows). <paramref name="syncNewBadge"/>
    /// keeps <c>Fish_Unread_</c> in sync the same way <see cref="ApplyRecipes"/> keeps
    /// <c>NewestRecipes_</c> in sync.
    /// </summary>
    public static void ApplyFishCaught(PlayerSaveData data, IReadOnlyList<string> fish, bool syncNewBadge = true)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (syncNewBadge) SyncNewBadge(root, "Compendium_Fish_", fish, "Fish_Unread_", FullNames.FishUnread);
        ReplaceNameArray(root, "Compendium_Fish_", fish, CreateWhenFilled(fish, FullNames.CompendiumFish));
    }
}
