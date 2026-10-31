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
public static partial class PlayerSaveWriter
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
        // Verbatim from the Cascade fixtures (an irradiated / a chilled character).
        public const string BodyTemperature = "BodyTemperature_15_B87CC427429084EE2405CA9F8B5AFB40";
        public const string Radiation = "Radiation_16_CBCAFEC549FC3D10D8D4418FD3D5FB77";
        // Active effects (BuffSave_Struct and its list), verbatim from the Cascade fixtures.
        public const string CurrentBuffDebuffs = "CurrentBuffDebuffs_150_9E6DA0704D0DE0DEF375ACA4CFD2D80A";
        public const string Buffs = "Buffs_8_2276236A4B2235B483FBBD8E932EAA87";
        public const string CurrentMoney = "CurrentMoney_85_7425E5BF43364C11279E4C8C26F5A7CA";
        // Verbatim from the newest-format fixture (Chrissie world player); absent from older saves.
        public const string LastHotbarSelection = "LastHotbarSelection_75_9D56EAE8464F9FFF52C04AA7B388D489";

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
        public const string TextureVariantRow = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636";

        // A character created without any traits never gets a Traits_ tag at all (an
        // empty array is the blueprint default), so ApplyTraits needs create-on-miss too.
        public const string Traits = "Traits_15_0039F2B34D2A43327122E9960B328E55";

        // Abiotic_CharacterSkill_Struct members (inside the Skills_ array). Verified
        // identical across all fixture player saves.
        public const string CurrentSkillXp = "CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556";
        public const string CurrentXPMultiplier = "CurrentXPMultiplier_15_9DA8B8A24B4F5B134743CDBE828520F0";

        // "NEW" badge arrays, the research queue and the two small CharacterSaveData
        // fields added for the badge-sync / research-queue feature. Harvested the same
        // way as the rest of this table (verified identical across every fixture player
        // save that carries the tag, including the newest -2146453647 build).
        public const string NewestRecipes = "NewestRecipes_141_C7277E904ACCEB6C26F1FB967CADCAFA";
        public const string CompendiumUnread = "Compendium_Unread_149_38D44AA044D8087E1B4026B3D3723202";
        public const string JournalUnread = "Journal_Unread_158_D78ED364437E5F530D2BD9A60F06DCE7";
        public const string FishUnread = "Fish_Unread_157_A91CA2374071D28DF6671F9816A63CA5";
        public const string ItemsDistilled = "ItemsDistilled_162_360CC93D4F1B4060A4AB61AC1E77FFC0";
        public const string RecipesRequiringResearch = "RecipesRequiringResearch_89_0A2778A74B6F1090075D8A9BEE7A0361";
        public const string CompletedIntro = "CompletedIntro_26_7F0FCDEA4BA0DD4D229BF38724FF442C";
        public const string LastControlRotation = "LastControlRotation_69_33E2359F425EBFDFB5CE2D84DCE6AD1B";

        // Unlock and codex lists the game omits until the first entry, harvested from every
        // older player save on hand (identical wherever the tag appears).
        public const string RecipesUnlock = "RecipesUnlock_41_C6D066A3416620A76188D2A39E4D8DF9";
        public const string EmailsRead = "EmailsRead_82_C3961BBA4AC266EDE4EC7A85E176A75E";
        public const string JournalEntries = "JournalEntries_79_C0F0FF544DFD1060769BBF9D2D90127C";
        public const string CompendiumEmailSections = "Compendium_EmailSections_117_5AF84C14476FC99020114C93ED0DBDA0";
        public const string CompendiumNarrativeSections = "Compendium_NarrativeSections_116_2FC6C70144FEDF460E5FE89B7CEA6B40";
        public const string CompendiumExplorationSections = "Compendium_ExplorationSections_118_50A35DD9425836A07E8AB1A66CB9A8DE";
        public const string CompendiumFish = "Compendium_Fish_130_F3328DBD41952E919E4BF48486764935";
        public const string CompendiumKillCount = "Compendium_KillCount_137_580C4765460383FB00B3A0B49694B010";
        public const string ItemsPickedUp = "ItemsPickedUp_40_201286D74B6DA7CECD1480A56F844497";
        public const string CraftedItems = "CraftedItems_102_5D7DF1C8402AF7997BA975ACE8206FAB";
        public const string MapsUnlocked = "MapsUnlocked_93_E9A91D554F338AD17C73E7A6EC41EB87";
    }

    /// <summary>
    /// Writes <paramref name="data"/>'s raw save to disk. The previous file content is
    /// preserved as <c>&lt;path&gt;.bak</c> so one bad write can't destroy a save.
    /// </summary>
    public static void WriteToFile(PlayerSaveData data, string path)
    {
        Compatibility.SaveWriteGuard.EnsureWritable(data.Raw);
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
}
