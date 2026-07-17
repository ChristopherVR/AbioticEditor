using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized override for <see cref="SkillMilestoneCatalog"/> (per-skill passive
/// text and milestone perk/effect prose). The Core catalog stays English (the CLI source of
/// truth); this maps a known skill + milestone level to a resx-backed translation, mirroring
/// the DoorLocalization pattern.
/// </summary>
public static class SkillLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    /// <summary>Localized per-level passive bonus text, or null when the skill isn't in the catalog.</summary>
    public static string? PassiveFor(string skillDisplayName)
        => SkillMilestoneCatalog.PassiveFor(skillDisplayName) is { } native
            ? Loc.GetOrNull($"Skill_Passive_{SkillKey(skillDisplayName)}") ?? native
            : null;

    /// <summary>
    /// Localized milestone perk track for a skill, in level order. When the milestones came
    /// from the game's own tables (<see cref="SkillMilestoneCatalog.HasLiveDataFor"/>) the
    /// game text is kept as-is - it already follows the game-data language - and the resx
    /// override only applies to the static English fallback. A missing key (a perk newer
    /// than the translations) falls back to the native text rather than showing a raw key.
    /// </summary>
    public static IReadOnlyList<SkillMilestone> MilestonesFor(string skillDisplayName)
    {
        var native = SkillMilestoneCatalog.For(skillDisplayName);
        if (native.Count == 0 || SkillMilestoneCatalog.HasLiveDataFor(skillDisplayName)) return native;

        var key = SkillKey(skillDisplayName);
        return [.. native
            .Select(m => m with
            {
                Perk = Loc.GetOrNull($"Skill_Milestone_{key}_{m.Level}_Perk") ?? m.Perk,
                Effect = Loc.GetOrNull($"Skill_Milestone_{key}_{m.Level}_Effect") ?? m.Effect,
            })];
    }

    // "Blunt Melee" -> "BluntMelee", "First Aid" -> "FirstAid"; matches resx key suffixes.
    private static string SkillKey(string skillDisplayName) => skillDisplayName.Replace(" ", "", StringComparison.Ordinal);
}
