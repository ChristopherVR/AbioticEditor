using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class PlayerRemainingSessionTests
{
    [Fact]
    public void Heal_and_max_skills_are_staged_and_revertible()
    {
        var session = OpenPlayer();
        var originalVitals = session.Vitals.Clone();
        var originalSkills = session.Skills.Select(skill => skill.ToPlayerSkill()).ToArray();

        session.Vitals.HealAll();
        session.MaxAllSkills();
        Assert.True(session.IsDirty);
        Assert.All(session.Skills, skill => Assert.Equal(SkillCatalog.MaxLevel, skill.Level));

        session.Revert();
        Assert.Equal(originalVitals.Head, session.Vitals.Head);
        Assert.Equal(originalVitals.Torso, session.Vitals.Torso);
        Assert.Equal(originalSkills.Select(skill => skill.Level), session.Skills.Select(skill => skill.Level));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Raw_editor_rejects_invalid_primitive_input_without_staging()
    {
        var session = OpenPlayer();
        var numeric = session.RawProperties.First(property => property.IsEditable && (property.Type.Contains("Int", StringComparison.OrdinalIgnoreCase) || property.Type.Contains("Float", StringComparison.OrdinalIgnoreCase) || property.Type.Contains("Double", StringComparison.OrdinalIgnoreCase)));

        Assert.False(session.TryStageRawEdit(numeric.Name, "not-a-number", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.False(session.IsDirty);
    }

    private static PlayerSaveSession OpenPlayer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        return new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path);
    }
}
