using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Read + write coverage for the positional skill model against the Cascade fixture saves.
/// </summary>
public class SkillEditTests
{
    private readonly ITestOutputHelper _output;

    public SkillEditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_FifteenPositionalSkills()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        Assert.Equal(SkillCatalog.Fallback.Count, data.Skills.Count);

        foreach (var skill in data.Skills)
        {
            var def = SkillCatalog.Fallback[skill.Index];
            _output.WriteLine($"[{skill.Index}] {def.DisplayName,-14} XP={skill.Xp,12:F1}  L{skill.Level}");
            Assert.InRange(skill.Level, 0, SkillCatalog.MaxLevel);
        }

        // This fixture is an end-game character: sprinting is maxed.
        Assert.Equal(20, data.Skills[0].Level);
    }

    [Fact]
    public void Reads_TraitsAndPhd()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        Assert.Equal("PhD_HumanBio", data.Phd);
        Assert.Contains("Trait_LeadBelly", data.Traits);
        foreach (var t in data.Traits)
        {
            _output.WriteLine($"{t} → {TraitCatalog.DisplayNameFor(t)}");
        }
    }

    [Fact]
    public void Fallback_FollowsInGamePanelOrder()
    {
        // The positional Skills_ array is serialized in the in-game panel order
        // (Fitness, Combat, Survival) - NOT DT_Skills row order. Strength is the second
        // entry (Fitness) and Accuracy sits in the Combat group; a regression that reverts
        // to DT_Skills order would swap them and mislabel every save.
        Assert.Equal(SkillCatalog.CanonicalOrder, SkillCatalog.Fallback.Select(d => d.Id).ToList());
        Assert.Equal("Sprinting", SkillCatalog.Fallback[0].Id);
        Assert.Equal("Strength", SkillCatalog.Fallback[1].Id);
        Assert.Equal("Accuracy", SkillCatalog.Fallback[6].Id);
        Assert.Equal("Fishing", SkillCatalog.Fallback[14].Id);

        for (var i = 0; i < SkillCatalog.Fallback.Count; i++)
        {
            Assert.Equal(i, SkillCatalog.Fallback[i].SaveIndex);
        }
    }

    [Fact]
    public void LoadFrom_PlacesGameSkillsInCanonicalOrder()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var defs = SkillCatalog.LoadFrom(provider);

        // The live DT_Skills load must be re-sequenced into the in-game panel order, not left
        // in DT_Skills row order. The first 15 entries must match the canonical order exactly.
        var ids = defs.Select(d => d.Id).Take(SkillCatalog.CanonicalOrder.Count).ToList();
        Assert.Equal(SkillCatalog.CanonicalOrder, ids);

        // Game-derived display names still come through (localization / mod support).
        Assert.Equal("Strength", defs[1].DisplayName);
        Assert.Equal("Accuracy", defs[6].DisplayName);
        for (var i = 0; i < defs.Count; i++)
        {
            Assert.Equal(i, defs[i].SaveIndex);
        }
    }

    [Fact]
    public void LevelMath_RoundTripsThresholds()
    {
        Assert.Equal(0, SkillCatalog.LevelForXp(0));
        Assert.Equal(0, SkillCatalog.LevelForXp(199));
        Assert.Equal(1, SkillCatalog.LevelForXp(200));
        Assert.Equal(19, SkillCatalog.LevelForXp(91_654));
        Assert.Equal(20, SkillCatalog.LevelForXp(91_655));
        Assert.Equal(20, SkillCatalog.LevelForXp(999_999));
        for (var level = 1; level <= SkillCatalog.MaxLevel; level++)
        {
            Assert.Equal(level, SkillCatalog.LevelForXp(SkillCatalog.XpForLevel(level)));
        }
    }

    [Fact]
    public void ApplySkills_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);

        // Drop the skill at index 2 to level 5 and bump its multiplier.
        var updated = data.Skills
            .Select(s => s.Index == 2 ? s with { Xp = SkillCatalog.XpForLevel(5), XpMultiplier = 1.5f } : s)
            .ToList();
        PlayerSaveWriter.ApplySkills(data, updated);

        // Round-trip through bytes and re-read.
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(SkillCatalog.XpForLevel(5), reloaded.Skills[2].Xp);
        Assert.Equal(1.5f, reloaded.Skills[2].XpMultiplier);
        Assert.Equal(5, reloaded.Skills[2].Level);

        // Untouched neighbours keep their values.
        Assert.Equal(data.Skills[0].Xp, reloaded.Skills[0].Xp);
        Assert.Equal(data.Skills[14].Xp, reloaded.Skills[14].Xp);
    }

    [Fact]
    public void ApplyTraits_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var newTraits = data.Traits.Where(t => t != "Trait_WeakBladder").Append("Trait_FannyPack").ToList();
        PlayerSaveWriter.ApplyTraits(data, newTraits);
        PlayerSaveWriter.ApplyPhd(data, "PhD_Intern");

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(newTraits, reloaded.Traits);
        Assert.Equal("PhD_Intern", reloaded.Phd);
    }
}
