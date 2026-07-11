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

        // A character created without any traits never gets a Traits_ tag at all (an
        // empty array is the blueprint default), so ApplyTraits needs create-on-miss too.
        public const string Traits = "Traits_15_0039F2B34D2A43327122E9960B328E55";

        // Abiotic_CharacterSkill_Struct members (inside the Skills_ array). Verified
        // identical across all fixture player saves.
        public const string CurrentSkillXp = "CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556";
        public const string CurrentXPMultiplier = "CurrentXPMultiplier_15_9DA8B8A24B4F5B134743CDBE828520F0";
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
}
