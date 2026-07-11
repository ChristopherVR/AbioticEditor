namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One entry of the save's positional <c>Skills_</c> array.
/// </summary>
/// <param name="Index">Array position - identity; see <see cref="SkillCatalog"/>.</param>
/// <param name="Xp">Cumulative XP (<c>CurrentSkillXP_</c>).</param>
/// <param name="XpMultiplier">Per-skill XP multiplier (<c>CurrentXPMultiplier_</c>), from job/trait bonuses.</param>
public sealed record PlayerSkill(int Index, float Xp, float XpMultiplier)
{
    public int Level => SkillCatalog.LevelForXp(Xp);
}
