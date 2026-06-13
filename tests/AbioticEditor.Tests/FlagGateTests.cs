using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class FlagGateTests
{
    [Fact]
    public void StoryTrigger_RequiresAllEarlierTriggers()
    {
        // Labs_Containment_Entered is chapter index 13 - every earlier trigger gates it.
        var prereqs = FlagGate.PrerequisitesFor("Labs_Containment_Entered");
        Assert.Equal(13, prereqs.Count);
        Assert.Equal("Office_NewGameStarted", prereqs[0]);
        Assert.Equal("Labs_MiddleProgression", prereqs[^1]);
    }

    [Fact]
    public void RegionFlag_RequiresItsRegionChapter()
    {
        // A non-story Labs flag needs the chapter that opens the Labs.
        var prereqs = FlagGate.PrerequisitesFor("LABS_SomeRandomDoorOpened");
        var only = Assert.Single(prereqs);
        Assert.Equal("Labs_MiddleProgression", only);
    }

    [Fact]
    public void OfficeAndUnmappedAreas_AreUngated()
    {
        Assert.Empty(FlagGate.PrerequisitesFor("Office_TutorialThing"));
        Assert.Empty(FlagGate.PrerequisitesFor("NightRealm_Entered"));
    }

    [Fact]
    public void RegionChapterForRowId_MapsEmailIdsByAreaPrefix()
    {
        var chapter = FlagGate.RegionChapterForRowId("Labs_Email_SomeLoreDump");
        Assert.NotNull(chapter);
        Assert.Equal("Labs", chapter!.Row);
    }
}
