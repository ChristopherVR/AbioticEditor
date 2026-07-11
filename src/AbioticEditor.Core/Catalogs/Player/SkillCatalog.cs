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
/// (<c>skill_sprinting</c>) - identity comes from array order alone.
///
/// The array is serialized in the game's on-screen skill order (the Fitness / Combat /
/// Survival grouping of the in-game skills panel), NOT <c>DT_Skills</c> row order. The two
/// orders differ: <c>DT_Skills</c> lists Accuracy second and Strength near the end, but the
/// game writes Strength second (Fitness) and Accuracy in the Combat group, so a catalog keyed
/// to <c>DT_Skills</c> mislabels every skill that moves between the two orders. The earlier
/// "verified against an end-game save" check could not catch this: in a fully maxed save every
/// position reads ~91,655 XP, so any ordering looks correct. The canonical order below is the
/// in-game panel order (see <see cref="CanonicalOrder"/>); identity is the array index.
/// Source: abioticfactor.wiki.gg/wiki/Skills.
/// </summary>
public static class SkillCatalog
{
    /// <summary>
    /// The in-game skill-panel order (Fitness, then Combat, then Survival) by <c>DT_Skills</c>
    /// row name. This is the order the game serializes the positional <c>Skills_</c> array, so it
    /// is the single source of truth for index -> skill identity, used by both
    /// <see cref="Fallback"/> and <see cref="LoadFrom"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> CanonicalOrder = new[]
    {
        // Fitness
        "Sprinting", "Strength", "Throwing", "Sneaking",
        // Combat
        "BluntMelee", "SharpMelee", "Accuracy", "Reloading", "Fortitude",
        // Survival
        "Crafting", "Construction", "FirstAid", "Cooking", "Agriculture", "Fishing",
    };

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
        // Fitness
        new SkillDefinition(0,  "Sprinting",    "Sprinting",    null, "/Game/Textures/GUI/SkillIcons/skillicon_sprinting.skillicon_sprinting"),
        new SkillDefinition(1,  "Strength",     "Strength",     null, "/Game/Textures/GUI/SkillIcons/skillicon_strength.skillicon_strength"),
        new SkillDefinition(2,  "Throwing",     "Throwing",     null, "/Game/Textures/GUI/SkillIcons/skillicon_throwing.skillicon_throwing"),
        new SkillDefinition(3,  "Sneaking",     "Sneaking",     null, "/Game/Textures/GUI/SkillIcons/skillicon_sneaking.skillicon_sneaking"),
        // Combat
        new SkillDefinition(4,  "BluntMelee",   "Blunt Melee",  null, "/Game/Textures/GUI/SkillIcons/skillicon_melee_blunt.skillicon_melee_blunt"),
        new SkillDefinition(5,  "SharpMelee",   "Sharp Melee",  null, "/Game/Textures/GUI/SkillIcons/skillicon_melee_sharp.skillicon_melee_sharp"),
        new SkillDefinition(6,  "Accuracy",     "Accuracy",     null, "/Game/Textures/GUI/SkillIcons/skillicon_accuracy.skillicon_accuracy"),
        new SkillDefinition(7,  "Reloading",    "Reloading",    null, "/Game/Textures/GUI/SkillIcons/skillicon_reloading.skillicon_reloading"),
        new SkillDefinition(8,  "Fortitude",    "Fortitude",    null, "/Game/Textures/GUI/SkillIcons/skillicon_fortitude.skillicon_fortitude"),
        // Survival
        new SkillDefinition(9,  "Crafting",     "Crafting",     null, "/Game/Textures/GUI/SkillIcons/skillicon_crafting.skillicon_crafting"),
        new SkillDefinition(10, "Construction", "Construction", null, "/Game/Textures/GUI/SkillIcons/skillicon_construction.skillicon_construction"),
        new SkillDefinition(11, "FirstAid",     "First Aid",    null, "/Game/Textures/GUI/SkillIcons/skillicon_firstaid.skillicon_firstaid"),
        new SkillDefinition(12, "Cooking",      "Cooking",      null, "/Game/Textures/GUI/SkillIcons/skillicon_cooking.skillicon_cooking"),
        new SkillDefinition(13, "Agriculture",  "Agriculture",  null, "/Game/Textures/GUI/SkillIcons/skillicon_agriculture.skillicon_agriculture"),
        new SkillDefinition(14, "Fishing",      "Fishing",      null, "/Game/Textures/GUI/Icons/icon_fishing.icon_fishing"),
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
    /// Loads skill display metadata (name, description, icon) from the game's <c>DT_Skills</c>
    /// DataTable and arranges it in the save's positional order (<see cref="CanonicalOrder"/>,
    /// the in-game panel order), skipping rows whose display name carries the DONOTUSE marker.
    /// Returns <see cref="Fallback"/> when mappings are missing or the table can't be read.
    /// </summary>
    /// <remarks>
    /// Skills are positional (identity = index in the save's <c>Skills_</c> array). That order is
    /// the in-game panel order, which is NOT the same as <c>DT_Skills</c> row order, so the table
    /// is used only for per-skill text/icons and is re-sequenced into <see cref="CanonicalOrder"/>.
    /// Rows the canonical order doesn't name (a mod or a newer game version that added a skill)
    /// are appended after the known ones in DT_Skills row order - a best-effort guess, since their
    /// true save position is unknown. A save whose array is longer than the catalog still
    /// round-trips safely via <see cref="WithUnknownPlaceholders"/>.
    /// </remarks>
    public static IReadOnlyList<SkillDefinition> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Fallback;

        try
        {
            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills");
            var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault();
            if (dt is null) return Fallback;

            // Gather the usable rows, preserving DT_Skills row order for any leftovers.
            var rows = new List<(string Id, string Display, string? Description, string? Icon)>(dt.RowMap.Count);
            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                byId[kv.Key.Text] = rows.Count;
                rows.Add((kv.Key.Text, display, description, icon));
            }
            if (rows.Count == 0) return Fallback;

            // Emit in canonical (in-game panel) order, then append any rows the canonical list
            // doesn't name (mod / newer-version skills) in their DT_Skills row order.
            var result = new List<SkillDefinition>(rows.Count);
            var used = new bool[rows.Count];
            foreach (var id in CanonicalOrder)
            {
                if (!byId.TryGetValue(id, out var idx)) continue;
                var r = rows[idx];
                used[idx] = true;
                result.Add(new SkillDefinition(result.Count, r.Id, r.Display, r.Description, r.Icon));
            }
            for (var i = 0; i < rows.Count; i++)
            {
                if (used[i]) continue;
                var r = rows[i];
                result.Add(new SkillDefinition(result.Count, r.Id, r.Display, r.Description, r.Icon));
            }
            return result.Count > 0 ? result : Fallback;
        }
        catch
        {
            return Fallback;
        }
    }
}
