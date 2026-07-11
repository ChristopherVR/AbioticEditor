using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - survival stats, skills, traits, limb health, and respawn edits.
public static partial class PlayerSaveWriter
{
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

    /// <summary>
    /// Patches the positional <c>Skills_</c> array from <paramref name="updated"/>.
    /// Skills are matched by array index; only <c>CurrentSkillXP_</c> and
    /// <c>CurrentXPMultiplier_</c> are written (the SkillName text fields are inert
    /// blueprint defaults the game ignores). Both fields sit at their blueprint default
    /// (0 XP, 1.0 multiplier) on an untouched skill and can be omitted from the save, so
    /// both use create-on-miss (see <see cref="FullNames"/>). Out-of-range entries are
    /// skipped.
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

            SetFloat(ps.Properties, "CurrentSkillXP_", skill.Xp, FullNames.CurrentSkillXp);
            SetFloat(ps.Properties, "CurrentXPMultiplier_", skill.XpMultiplier, FullNames.CurrentXPMultiplier);
        }
    }

    /// <summary>
    /// Replaces the <c>Traits_</c> name array with <paramref name="traits"/> (internal
    /// row names like <c>Trait_LeadBelly</c>). Mirrors the WorldFlags writer: an existing
    /// ArrayProperty instance is kept, only its element buffer is swapped. Unlike WorldFlags,
    /// a missing tag is created (see <see cref="FullNames.Traits"/>): a character that
    /// started with no traits never gets a <c>Traits_</c> tag at all, so a plain prefix
    /// lookup would silently no-op on every trait added to such a character.
    /// </summary>
    public static void ApplyTraits(PlayerSaveData data, IReadOnlyList<string> traits)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var array = FindOrCreateNameArray(root, "Traits_", FullNames.Traits);
        if (array is null) return;

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
}
