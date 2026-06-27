using System.IO;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class StoryProgressionTests
{
    private static string? MetadataSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_StoryProgressionAndPlaytime()
    {
        var path = MetadataSave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        Assert.Equal("Voussoir", data.StoryProgressionRow);
        Assert.True(data.MinutesPassed > 0);
        Assert.True(StoryProgressionCatalog.IndexOf(data.StoryProgressionRow) >= 0);
    }

    [Fact]
    public void RegionSaves_HaveNoStoryProgression()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_H_Cabin.sav");
        if (!File.Exists(path)) return;

        var data = WorldSaveReader.ReadFromFile(path);
        Assert.Null(data.StoryProgressionRow);
        Assert.Null(data.MinutesPassed);
    }

    [Fact]
    public void ApplyStoryProgression_RoundTripsThroughSerializer()
    {
        var path = MetadataSave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        WorldSaveWriter.ApplyStoryProgression(data, "EndGame");
        WorldSaveWriter.ApplyMinutesPassed(data, 123);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        Assert.Equal("EndGame", reloaded.StoryProgressionRow);
        Assert.Equal(123, reloaded.MinutesPassed);
    }

    // ---------- progress-implies-earlier-milestones (trader gating) ----------

    [Fact]
    public void ChapterIndexForFlag_MapsTriggerFlags_AndRejectsNonTriggers()
    {
        Assert.Equal(0, StoryProgressionCatalog.ChapterIndexForFlag("Office_NewGameStarted"));
        Assert.True(StoryProgressionCatalog.ChapterIndexForFlag("EndBossDefeated") > 0);
        // Office_TalkedToWarren is a real flag but not a chapter trigger, so it has no chapter index.
        Assert.Equal(-1, StoryProgressionCatalog.ChapterIndexForFlag("Office_TalkedToWarren"));
        Assert.Equal(-1, StoryProgressionCatalog.ChapterIndexForFlag("NotAFlagAtAll"));
    }

    [Fact]
    public void FurthestReachedIndex_TakesTheHighestSetTrigger_AndIsNegativeWhenEmpty()
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Office_NewGameStarted", "Office_ThirdFloorReached", "Office_Silo3Opened",
        };
        var furthest = StoryProgressionCatalog.FurthestReachedIndex(flags.Contains);

        // Office_Silo3Opened triggers the Flathill chapter - further than the Office start.
        Assert.True(furthest >= StoryProgressionCatalog.IndexOf("Flathill"));
        Assert.True(furthest > StoryProgressionCatalog.IndexOf("Office"));
        Assert.Equal(-1, StoryProgressionCatalog.FurthestReachedIndex(_ => false));
    }

    [Fact]
    public void WarrenScenario_StoryProgressSatisfiesAnEarlyGate_WithoutTheExactFlag()
    {
        // Reproduces the user's Game Pass save: well past Warren (third floor, silo) but the specific
        // "talked to Warren" flag never persisted. Warren's gate is the Office chapter trigger, so a
        // world this far in should clear his gate even though only later milestones are set.
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Office_NewGameStarted", "Office_ThirdFloorReached", "Office_Silo3Opened",
            "Office_CafeteriaQuestStarted",
        };
        var gateChapter = StoryProgressionCatalog.ChapterIndexForFlag("Office_NewGameStarted");
        Assert.True(gateChapter >= 0);
        Assert.True(StoryProgressionCatalog.FurthestReachedIndex(flags.Contains) >= gateChapter);
    }
}
