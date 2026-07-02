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
    public void RegionChapterForRowId_MapsJournalIdsByAreaPrefix()
    {
        // Journal ids lead with the region directly (verified: "Labs_AbeandJanet" is a real
        // JournalEntries_ row from a completed-game fixture).
        var chapter = FlagGate.RegionChapterForRowId("Labs_AbeandJanet");
        Assert.NotNull(chapter);
        Assert.Equal("Labs", chapter!.Row);
    }

    [Fact]
    public void RegionChapterForRowId_MapsEmailIdsPastTheEmailMarker()
    {
        // Email ids lead with "Email_"/"email_" BEFORE the region, not after (verified: real
        // save fixtures use "Email_Labs_Kizz" / "email_labs_creepingcrystal", never the reverse
        // "Labs_Email_..." order the row-id doc previously assumed).
        var upper = FlagGate.RegionChapterForRowId("Email_Labs_Kizz");
        Assert.NotNull(upper);
        Assert.Equal("Labs", upper!.Row);

        var lower = FlagGate.RegionChapterForRowId("email_labs_creepingcrystal");
        Assert.NotNull(lower);
        Assert.Equal("Labs", lower!.Row);

        // Emails with no embedded region (the majority) stay ungated rather than mismatching.
        Assert.Null(FlagGate.RegionChapterForRowId("Email_Random_IsWrestlingReal"));
        Assert.Null(FlagGate.RegionChapterForRowId("email_vacuum"));
    }
}
