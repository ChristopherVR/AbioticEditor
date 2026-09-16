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
        ReplaceNameArray(root, "RecipesUnlock_", recipes);
    }

    /// <summary>Replaces the <c>EmailsRead_</c> name array.</summary>
    public static void ApplyEmailsRead(PlayerSaveData data, IReadOnlyList<string> emails)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        ReplaceNameArray(root, "EmailsRead_", emails);
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
        ReplaceNameArray(root, "JournalEntries_", journals);
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

    /// <summary>
    /// Replaces the <c>Compendium_Fish_</c> name array (DT_Fish rows). <paramref name="syncNewBadge"/>
    /// keeps <c>Fish_Unread_</c> in sync the same way <see cref="ApplyRecipes"/> keeps
    /// <c>NewestRecipes_</c> in sync.
    /// </summary>
    public static void ApplyFishCaught(PlayerSaveData data, IReadOnlyList<string> fish, bool syncNewBadge = true)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (syncNewBadge) SyncNewBadge(root, "Compendium_Fish_", fish, "Fish_Unread_", FullNames.FishUnread);
        ReplaceNameArray(root, "Compendium_Fish_", fish);
    }
}
