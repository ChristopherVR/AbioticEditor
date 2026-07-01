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
    public void PrerequisitesFor_IncludesGranularQuestDependencies_Transitively()
    {
        // CafeteriaUnlocked depends on CafeteriaQuestStarted (you can't finish a quest you never
        // started). The prereq list must surface that granular step, not just chapter triggers.
        var prereqs = FlagGate.PrerequisitesFor("Office_CafeteriaUnlocked");
        Assert.Contains("Office_CafeteriaQuestStarted", prereqs);

        // The forklift door needs both the forklift and the power cells found.
        var fork = FlagGate.PrerequisitesFor("Office_ForkliftDoorOpened");
        Assert.Contains("Office_ForkliftFound", fork);
        Assert.Contains("Office_PowerCellFound", fork);

        // Transitive: completing Anteverse B requires it fixed, which requires its portal opened.
        var anteverse = FlagGate.PrerequisitesFor("LABS_CompletedAnteverseB");
        Assert.Contains("LABS_AnteverseBFixed", anteverse);
        Assert.Contains("LABS_OpenAnteversePortal", anteverse);

        // The pumps-fixed milestone requires each of the three pumps.
        var pumps = FlagGate.PrerequisitesFor("MF_PumpsFixed");
        Assert.Contains("MF_RedPumpFixed", pumps);
        Assert.Contains("MF_TealPumpFixed", pumps);
        Assert.Contains("MF_YellowPumpFixed", pumps);
    }

    [Fact]
    public void PrerequisitesFor_FollowsDeepChains_AcrossManufacturingAndLabs()
    {
        // Manufacturing NPC chain: meeting the soldier presupposes meeting Varsha.
        Assert.Contains("MF_MetVarsha", FlagGate.PrerequisitesFor("MF_MetSoldier"));

        // Labs spine: opening the vacuum (exit) door pulls in the whole Anteverse/turret chain.
        var vacuum = FlagGate.PrerequisitesFor("LABS_OpenVacuumDoor");
        Assert.Contains("LABS_CompletedAnteverseB", vacuum);
        Assert.Contains("LABS_AnteverseBFixed", vacuum);
        Assert.Contains("LABS_OpenAnteversePortal", vacuum);
        Assert.Contains("LABS_TurretsDeactivated", vacuum);

        // Office: the forklift door transitively needs the Silo 3 portal (where the power cell is found).
        Assert.Contains("Office_Silo3PortalOpened", FlagGate.PrerequisitesFor("Office_ForkliftDoorOpened"));
    }

    [Fact]
    public void PrerequisitesFor_HasNoSelfCycles_AcrossEveryCuratedFlag()
    {
        // The transitive expansion mixes chapter rules with the curated dependency graph; guard that
        // no flag ever ends up listing itself (a cycle would also be a sign of a bad edge).
        foreach (var flag in QuestFlagDependencies.Direct.Keys)
        {
            var prereqs = FlagGate.PrerequisitesFor(flag);
            Assert.DoesNotContain(flag, prereqs);
        }
    }

    [Fact]
    public void QuestDependencies_RecordTheCafeteriaConsequence_JagerDiesAndDoorOpens()
    {
        Assert.True(QuestFlagDependencies.Consequences.TryGetValue("Office_CafeteriaUnlocked", out var c));
        Assert.Contains("Jager", c!.NpcDeaths);
        Assert.Contains("Cafeteria", c.DoorsOpened);
    }

    // ---------- reverting a chapter should clear granular quest steps too, not just triggers ----------

    [Fact]
    public void DependentsOf_FindsTheWholeFinaleChain_BuiltOnTopOfAChapterTrigger()
    {
        // A real completed save has End_MainStoryComplete set alongside the whole boss/epilogue
        // chain. None of that is a DT_StoryProgression trigger, so before this graph existed nothing
        // ever cleared it on a story rewind - the game kept reading the save as finished. Rooted at
        // Residence_Fracture_Complete (the SouthIsland trigger the whole finale sits on top of) plus
        // EndBossDefeated (the EndGame trigger itself) - exactly what ClearForwardFlags seeds with
        // when rewinding to any chapter before SouthIsland.
        var completed = new[]
        {
            "Residence_Fracture_Complete", "EndBossDefeated", "EndBossPhase1Complete", "EndBossPhase2Complete",
            "End_Boss_FightStartedOnce", "End_Contact", "End_PostBoss_MetWitch_Island", "End_PostBoss_MetCahn_Island",
            "End_PostBoss_MetRiggs_Island", "End_PostBoss_MetJanet_Island", "End_MainStoryComplete",
            "Office_NewGameStarted", // an earlier, unrelated flag that must survive
        };

        var roots = new[] { "Residence_Fracture_Complete", "EndBossDefeated" };
        var dependents = FlagGate.DependentsOf(roots, completed);

        Assert.Contains("End_MainStoryComplete", dependents);
        Assert.Contains("End_PostBoss_MetJanet_Island", dependents);
        Assert.Contains("EndBossPhase1Complete", dependents);
        Assert.DoesNotContain("Office_NewGameStarted", dependents);
    }

    [Fact]
    public void DependentsOf_OnlyReturnsFlagsThatAreActuallySet()
    {
        // Reactors_SG_CableBreak2 depends on Reactors_SG_Interior, but it was never set in this
        // world - the cascade must not invent it just because the dependency graph mentions it.
        var partial = new[] { "Reactors_SG_Opened", "Reactors_SG_Entered", "Reactors_SG_Interior" };
        var roots = new[] { "Reactors_SG_Opened" };
        var dependents = FlagGate.DependentsOf(roots, partial);

        Assert.Contains("Reactors_SG_Interior", dependents);
        Assert.DoesNotContain("Reactors_SG_CableBreak2", dependents);
        Assert.DoesNotContain("Reactors_SG_ExitOpened", dependents);
    }

    [Fact]
    public void ClearForwardFlags_OnARealCompletedSave_ClearsTheFinaleAndReactorChain()
    {
        // tests/fixtures/DedicatedServerSaves/Worlds/Cascade is a real, fully completed game
        // (StoryProgressionRow == EndGame, End_MainStoryComplete set). Rewinding it to the chapter
        // where the Reactor Sector opens must clear not just the trigger flags of every later
        // chapter, but the granular steps built on top of them - otherwise the game still reads the
        // reverted save as finished.
        if (Fixtures.ServerWorldsDir is null) return;
        var metaSrc = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_MetaData.sav");
        var facilitySrc = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_Facility.sav");
        if (!File.Exists(metaSrc) || !File.Exists(facilitySrc)) return;

        var dir = Directory.CreateTempSubdirectory("story-revert");
        try
        {
            var metaCopy = Path.Combine(dir.FullName, "WorldSave_MetaData.sav");
            var facilityCopy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");
            File.Copy(metaSrc, metaCopy);
            File.Copy(facilitySrc, facilityCopy);

            var before = WorldSaveReader.ReadFromFile(facilityCopy);
            Assert.Contains("End_MainStoryComplete", before.Flags);
            Assert.Contains("Reactors_SG_CableBreak1", before.Flags);

            var (removed, _) = StoryFlagSync.ClearForwardFlags(metaCopy, "ReactorsEntry");
            Assert.True(removed > 0);

            var after = WorldSaveReader.ReadFromFile(facilityCopy);
            // The finale chain, several steps removed from any single chapter trigger.
            Assert.DoesNotContain("End_MainStoryComplete", after.Flags);
            Assert.DoesNotContain("EndBossDefeated", after.Flags);
            Assert.DoesNotContain("End_PostBoss_MetJanet_Island", after.Flags);
            // Granular Reactor/Residence/Fracture steps, not just the chapter triggers.
            Assert.DoesNotContain("Reactors_SG_CableBreak1", after.Flags);
            Assert.DoesNotContain("Reactors_S2War_Complete", after.Flags);
            Assert.DoesNotContain("Res_Objective1_A", after.Flags);
            Assert.DoesNotContain("Fracture_DL_AllEyes_Destroyed", after.Flags);
            // Everything up to and including Office/Manufacturing/Labs/Security/Dams survives.
            Assert.Contains("Office_NewGameStarted", after.Flags);
            Assert.Contains("Dams_DarkWaterDrained", after.Flags);
        }
        finally { dir.Delete(recursive: true); }
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
