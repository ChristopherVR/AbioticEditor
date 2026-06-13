using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Static definition of one player skill: its save-array position, identity and visuals.
/// </summary>
/// <param name="SaveIndex">Position of this skill in the save's <c>Skills_</c> array.</param>
/// <param name="Id">DT_Skills row name, e.g. <c>SharpMelee</c>.</param>
/// <param name="DisplayName">Human name, e.g. <c>Sharp Melee</c>.</param>
/// <param name="Description">Tooltip text from DT_Skills (null for the static fallback).</param>
/// <param name="IconAssetPath">UE object path of the skill icon, <c>/Game/...</c> form.</param>
public sealed record SkillDefinition(
    int SaveIndex,
    string Id,
    string DisplayName,
    string? Description,
    string? IconAssetPath);

/// <summary>
/// The ordered list of player skills plus level/XP math.
///
/// Player saves store skills positionally: the <c>Skills_</c> array in
/// <c>CharacterSaveData</c> carries one <c>Abiotic_CharacterSkill_Struct</c> per skill,
/// and every entry's <c>SkillName</c> field is the compiler default
/// (<c>skill_sprinting</c>) - identity comes from array order alone. The order matches
/// <c>DT_Skills</c> row order with the two <c>DONOTUSE</c> rows (Engineering, Resilience)
/// removed; verified against a finished end-game save where every maxed skill sits just
/// above the level-20 XP threshold.
/// </summary>
public static class SkillCatalog
{
    /// <summary>
    /// Cumulative XP required to reach each level. Index 0 = level 1 (200 XP) ... index 19
    /// = level 20 (91,655 XP). Source: abioticfactor.wiki.gg/wiki/Skills (v1.0), confirmed
    /// against capped end-game save values.
    /// </summary>
    public static readonly IReadOnlyList<float> XpThresholds = new float[]
    {
        200, 500, 940, 1_572, 2_464, 3_699, 5_379, 7_587, 10_417, 13_950,
        18_242, 23_307, 29_101, 35_776, 43_310, 51_631, 60_608, 70_354, 80_755, 91_655,
    };

    public const int MaxLevel = 20;

    /// <summary>Level reached at <paramref name="xp"/> (0 when below the level-1 threshold).</summary>
    public static int LevelForXp(double xp)
    {
        for (var i = XpThresholds.Count - 1; i >= 0; i--)
        {
            if (xp >= XpThresholds[i]) return i + 1;
        }
        return 0;
    }

    /// <summary>Minimum cumulative XP for <paramref name="level"/> (0 for level 0).</summary>
    public static float XpForLevel(int level)
    {
        if (level <= 0) return 0;
        var idx = Math.Min(level, MaxLevel) - 1;
        return XpThresholds[idx];
    }

    /// <summary>
    /// Built-in skill table (DT_Skills order, DONOTUSE rows removed). Used when the game
    /// assets aren't available; <see cref="LoadFrom"/> supersedes it when they are.
    /// </summary>
    public static IReadOnlyList<SkillDefinition> Fallback { get; } = new[]
    {
        new SkillDefinition(0,  "Sprinting",    "Sprinting",    null, "/Game/Textures/GUI/SkillIcons/skillicon_sprinting.skillicon_sprinting"),
        new SkillDefinition(1,  "Accuracy",     "Accuracy",     null, "/Game/Textures/GUI/SkillIcons/skillicon_accuracy.skillicon_accuracy"),
        new SkillDefinition(2,  "Reloading",    "Reloading",    null, "/Game/Textures/GUI/SkillIcons/skillicon_reloading.skillicon_reloading"),
        new SkillDefinition(3,  "Sneaking",     "Sneaking",     null, "/Game/Textures/GUI/SkillIcons/skillicon_sneaking.skillicon_sneaking"),
        new SkillDefinition(4,  "SharpMelee",   "Sharp Melee",  null, "/Game/Textures/GUI/SkillIcons/skillicon_melee_sharp.skillicon_melee_sharp"),
        new SkillDefinition(5,  "BluntMelee",   "Blunt Melee",  null, "/Game/Textures/GUI/SkillIcons/skillicon_melee_blunt.skillicon_melee_blunt"),
        new SkillDefinition(6,  "Fishing",      "Fishing",      null, "/Game/Textures/GUI/Icons/icon_fishing.icon_fishing"),
        new SkillDefinition(7,  "Crafting",     "Crafting",     null, "/Game/Textures/GUI/SkillIcons/skillicon_crafting.skillicon_crafting"),
        new SkillDefinition(8,  "Construction", "Construction", null, "/Game/Textures/GUI/SkillIcons/skillicon_construction.skillicon_construction"),
        new SkillDefinition(9,  "FirstAid",     "First Aid",    null, "/Game/Textures/GUI/SkillIcons/skillicon_firstaid.skillicon_firstaid"),
        new SkillDefinition(10, "Agriculture",  "Agriculture",  null, "/Game/Textures/GUI/SkillIcons/skillicon_agriculture.skillicon_agriculture"),
        new SkillDefinition(11, "Cooking",      "Cooking",      null, "/Game/Textures/GUI/SkillIcons/skillicon_cooking.skillicon_cooking"),
        new SkillDefinition(12, "Fortitude",    "Fortitude",    null, "/Game/Textures/GUI/SkillIcons/skillicon_fortitude.skillicon_fortitude"),
        new SkillDefinition(13, "Strength",     "Strength",     null, "/Game/Textures/GUI/SkillIcons/skillicon_strength.skillicon_strength"),
        new SkillDefinition(14, "Throwing",     "Throwing",     null, "/Game/Textures/GUI/SkillIcons/skillicon_throwing.skillicon_throwing"),
    };

    /// <summary>
    /// Pads <paramref name="definitions"/> with "Unknown skill #N" placeholders so a
    /// save whose positional <c>Skills_</c> array is LONGER than the known catalog
    /// (newer game version added a skill) still shows - and round-trips - every entry.
    /// Returns <paramref name="definitions"/> unchanged when nothing is missing.
    /// </summary>
    public static IReadOnlyList<SkillDefinition> WithUnknownPlaceholders(
        IReadOnlyList<SkillDefinition> definitions, int skillCount)
    {
        if (skillCount <= definitions.Count) return definitions;

        var result = new List<SkillDefinition>(skillCount);
        result.AddRange(definitions);
        for (var i = definitions.Count; i < skillCount; i++)
        {
            result.Add(new SkillDefinition(
                i,
                $"UnknownSkill{i}",
                $"Unknown skill #{i}",
                "This save has a skill the editor's catalog doesn't know (added by a newer game version?). " +
                "Its XP is preserved and written back at the same position.",
                null));
        }
        return result;
    }

    /// <summary>
    /// Loads skill definitions from the game's <c>DT_Skills</c> DataTable, preserving row
    /// order and skipping rows whose display name carries the DONOTUSE marker. Returns
    /// <see cref="Fallback"/> when mappings are missing or the table can't be read.
    /// </summary>
    public static IReadOnlyList<SkillDefinition> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Fallback;

        try
        {
            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills");
            var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault();
            if (dt is null) return Fallback;

            var result = new List<SkillDefinition>(dt.RowMap.Count);
            foreach (var kv in dt.RowMap)
            {
                string? display = null, description = null, icon = null;
                foreach (var p in kv.Value.Properties)
                {
                    switch (p.Name.Text)
                    {
                        case "DisplayName": display = p.Tag?.GenericValue?.ToString(); break;
                        case "DisplayDescription": description = p.Tag?.GenericValue?.ToString(); break;
                        case "Icon": icon = p.Tag?.GenericValue?.ToString(); break;
                    }
                }
                if (display is null || display.Contains("DONOTUSE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result.Add(new SkillDefinition(result.Count, kv.Key.Text, display, description, icon));
            }
            return result.Count > 0 ? result : Fallback;
        }
        catch
        {
            return Fallback;
        }
    }
}
