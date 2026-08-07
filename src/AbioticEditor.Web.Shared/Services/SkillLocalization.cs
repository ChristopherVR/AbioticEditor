using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Web-hosted localized override for <see cref="SkillMilestoneCatalog"/> (per-skill passive
/// text and milestone perk/effect prose). Mirrors the desktop app's <c>SkillLocalization</c>
/// but reads through <see cref="HostLanguageService"/>'s resx catalog instead of MAUI's
/// resource manager. The Core catalog stays English (the CLI source of truth); this maps a
/// known skill + milestone level to a resx-backed translation.
/// </summary>
public static class SkillLocalization
{
    /// <summary>Localized per-level passive bonus text, or null when the skill isn't in the catalog.</summary>
    public static string? PassiveFor(HostLanguageService languages, string skillDisplayName)
        => SkillMilestoneCatalog.PassiveFor(skillDisplayName) is { } native
            ? languages.ResourceOrNull($"Skill_Passive_{SkillKey(skillDisplayName)}") ?? native
            : null;

    /// <summary>
    /// Localized milestone perk track for a skill, in level order. When the milestones came
    /// from the game's own tables (<see cref="SkillMilestoneCatalog.HasLiveDataFor"/>) the
    /// game text is kept as-is - it already follows the game-data language - and the resx
    /// override only applies to the static English fallback. A missing key (a perk newer
    /// than the translations) falls back to the native text rather than showing a raw key.
    /// </summary>
    public static IReadOnlyList<SkillMilestone> MilestonesFor(HostLanguageService languages, string skillDisplayName)
    {
        var native = SkillMilestoneCatalog.For(skillDisplayName);
        if (native.Count == 0 || SkillMilestoneCatalog.HasLiveDataFor(skillDisplayName)) return native;

        var key = SkillKey(skillDisplayName);
        return [.. native
            .Select(m => m with
            {
                Perk = languages.ResourceOrNull($"Skill_Milestone_{key}_{m.Level}_Perk") ?? m.Perk,
                Effect = languages.ResourceOrNull($"Skill_Milestone_{key}_{m.Level}_Effect") ?? m.Effect,
            })];
    }

    // "Blunt Melee" -> "BluntMelee", "First Aid" -> "FirstAid"; matches resx key suffixes.
    private static string SkillKey(string skillDisplayName) => skillDisplayName.Replace(" ", "", StringComparison.Ordinal);

    /// <summary>The spoiler-reveal key for one milestone, shared by the SKILLS tab chips and
    /// the sidebar's milestone detail panel so a reveal applies to both.</summary>
    public static string MilestoneSpoilerKey(PlayerSkillEdit skill, SkillMilestone milestone)
        => $"skill:{skill.Definition.DisplayName}:{milestone.Level}";

    /// <summary>In-game a perk stays hidden until its level is reached; a locked milestone is
    /// concealed while spoiler protection is on (native spoiler seal).</summary>
    public static bool IsMilestoneConcealed(HostSpoilerPreferences spoilers, PlayerSkillEdit skill, SkillMilestone milestone)
        => spoilers.Enabled && !skill.IsUnlocked(milestone) && !spoilers.IsRevealed(MilestoneSpoilerKey(skill, milestone));

    /// <summary>How far this skill is from the milestone (or confirmation it's unlocked).
    /// Mirrors the desktop app's SkillMilestoneViewModel.RequirementText.</summary>
    public static string MilestoneRequirementText(HostLanguageService languages, PlayerSkillEdit skill, SkillMilestone milestone)
    {
        if (skill.IsUnlocked(milestone)) return languages.Resource("Skill_Unlocked", skill.Level, milestone.Level);
        var levelsToGo = milestone.Level - skill.Level;
        var xpToGo = SkillCatalog.XpForLevel(milestone.Level) - skill.Xp;
        var levels = levelsToGo == 1 ? languages.Resource("Skill_OneLevel") : languages.Resource("Skill_LevelCount", levelsToGo);
        return languages.Resource("Skill_Locked", milestone.Level, levels, $"{Math.Max(0, xpToGo):F0}");
    }
}
