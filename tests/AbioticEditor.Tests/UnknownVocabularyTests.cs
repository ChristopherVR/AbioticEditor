using System.IO;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Saves from newer game versions can carry vocabulary our catalogs don't know:
/// extra positional skills, unknown fish/email/journal/compendium rows. None of it
/// may be dropped, and skills must surface as labeled placeholders.
/// </summary>
public class UnknownVocabularyTests
{
    private readonly ITestOutputHelper _output;

    public UnknownVocabularyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    // ---------- (a) skills ----------

    [Fact]
    public void SkillCatalog_PadsLongerSaveArrays_WithUnknownPlaceholders()
    {
        var defs = SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, 17);

        Assert.Equal(17, defs.Count);
        // Known skills are untouched.
        Assert.Equal("Sprinting", defs[0].Id);
        Assert.Equal("Throwing", defs[14].Id);
        // Extras are labeled placeholders at their save positions.
        Assert.Equal(15, defs[15].SaveIndex);
        Assert.Equal("Unknown skill #15", defs[15].DisplayName);
        Assert.Equal("Unknown skill #16", defs[16].DisplayName);
        Assert.Null(defs[16].IconAssetPath);
    }

    [Fact]
    public void SkillCatalog_ReturnsCatalogUnchanged_WhenNothingMissing()
    {
        Assert.Same(SkillCatalog.Fallback, SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, 15));
        Assert.Same(SkillCatalog.Fallback, SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, 3));
    }

    [Fact]
    public void Skills_LongerThanCatalog_AreReadAndWrittenPositionally()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        // Grow the save's positional Skills_ array by one synthetic entry (clone-by-
        // reference of the last struct is fine for serialization purposes).
        var data = PlayerSaveReader.ReadFromFile(path!);
        var baselineCount = data.Skills.Count;
        Assert.Equal(15, baselineCount);

        var array = GetSkillsArray(data.Raw);
        var grown = Array.CreateInstance(array.Value!.GetType().GetElementType()!, baselineCount + 1);
        Array.Copy(array.Value!, grown, baselineCount);
        grown.SetValue(array.Value!.GetValue(baselineCount - 1), baselineCount);
        array.Value = grown;

        // The reader surfaces all 16 entries, none dropped.
        var reread = Reserialize(data.Raw, PlayerSaveReader.ReadFromStream);
        Assert.Equal(16, reread.Skills.Count);

        // The catalog maps the extra entry to a labeled placeholder.
        var defs = SkillCatalog.WithUnknownPlaceholders(SkillCatalog.Fallback, reread.Skills.Count);
        Assert.Equal("Unknown skill #15", defs[15].DisplayName);

        // The writer keeps writing the extra entry positionally.
        var updated = reread.Skills.Select(s => new PlayerSkill(s.Index, 1000f + s.Index, 1f)).ToList();
        PlayerSaveWriter.ApplySkills(reread, updated);

        var final = Reserialize(reread.Raw, PlayerSaveReader.ReadFromStream);
        Assert.Equal(16, final.Skills.Count);
        Assert.Equal(1000f, final.Skills[0].Xp);
        Assert.Equal(1015f, final.Skills[15].Xp);
        _output.WriteLine($"16-skill save round-tripped; skill 15 XP = {final.Skills[15].Xp}");
    }

    // ---------- (b) fish + (c) emails / journals / compendium ----------

    [Fact]
    public void UnknownRows_FishEmailsJournalsCompendium_RoundTripThroughWriters()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);

        const string fish = "Fish_FutureSpecies_XYZ";
        const string email = "Email_FromTheFuture_XYZ";
        const string journal = "Journal_FromTheFuture_XYZ";
        const string comp = "Compendium_FromTheFuture_XYZ";

        PlayerSaveWriter.ApplyFishCaught(data, data.FishCaught.Append(fish).ToList());
        PlayerSaveWriter.ApplyEmailsRead(data, data.EmailsRead.Append(email).ToList());
        PlayerSaveWriter.ApplyJournals(data, data.Journals.Append(journal).ToList());
        PlayerSaveWriter.ApplyCompendium(
            data,
            data.CompendiumEmail.Append(comp).ToList(),
            data.CompendiumNarrative,
            data.CompendiumExploration);

        var reread = Reserialize(data.Raw, PlayerSaveReader.ReadFromStream);

        // Unknown ids survive verbatim alongside the original rows.
        Assert.Contains(fish, reread.FishCaught);
        Assert.Contains(email, reread.EmailsRead);
        Assert.Contains(journal, reread.Journals);
        Assert.Contains(comp, reread.CompendiumEmail);
        // `data`'s typed lists still hold the pre-edit values (the writer mutates the
        // raw tree, not the snapshot), so the reread arrays are exactly one longer.
        Assert.Equal(data.FishCaught.Count + 1, reread.FishCaught.Count);
        Assert.Equal(data.EmailsRead.Count + 1, reread.EmailsRead.Count);
        Assert.Equal(data.Journals.Count + 1, reread.Journals.Count);
        Assert.Equal(data.CompendiumEmail.Count + 1, reread.CompendiumEmail.Count);
        Assert.Equal(data.CompendiumNarrative.Count, reread.CompendiumNarrative.Count);
        Assert.Equal(data.CompendiumExploration.Count, reread.CompendiumExploration.Count);
        // Every original row also survived.
        foreach (var f in data.FishCaught) Assert.Contains(f, reread.FishCaught);
        foreach (var e in data.EmailsRead) Assert.Contains(e, reread.EmailsRead);
    }

    // ---------- helpers ----------

    private static PlayerSaveData Reserialize(SaveGame save, Func<Stream, PlayerSaveData> read)
    {
        using var ms = new MemoryStream();
        save.WriteTo(ms);
        ms.Position = 0;
        return read(ms);
    }

    private static ArrayProperty GetSkillsArray(SaveGame save)
    {
        var top = save.Properties!.First(t =>
            t.Name!.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal));
        var ps = (PropertiesStruct)((StructProperty)top.Property!).Value!;
        var tag = ps.Properties.First(t =>
            t.Name!.Value.StartsWith("Skills_", StringComparison.Ordinal));
        return (ArrayProperty)tag.Property!;
    }
}
