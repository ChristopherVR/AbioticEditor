using System.IO;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class DoorAndFlagCatalogTests
{
    private readonly ITestOutputHelper _output;

    public DoorAndFlagCatalogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FacilitySavePath => Path.Combine(
        Fixtures.CascadeDir ?? string.Empty,
        "WorldSave_Facility.sav");

    [Fact]
    public void DoorStateNames_ResolvesKnownValues()
    {
        // The seven runtime enum values + E_MAX sentinel observed in
        // AbioticFactor/Content/Blueprints/Data/E_DoorStates.uasset.
        var closed = DoorStateNames.Friendly("E_DoorStates::NewEnumerator0");
        var open = DoorStateNames.Friendly("E_DoorStates::NewEnumerator1");
        var locked = DoorStateNames.Friendly("E_DoorStates::NewEnumerator2");

        Assert.False(string.IsNullOrEmpty(closed));
        Assert.False(string.IsNullOrEmpty(open));
        Assert.False(string.IsNullOrEmpty(locked));
        Assert.NotEqual(closed, open);
        Assert.NotEqual(closed, locked);

        // Out-of-range and unknown forms still produce a non-null label.
        Assert.Equal("State 99", DoorStateNames.Friendly("E_DoorStates::NewEnumerator99"));
        Assert.Equal("Closed", DoorStateNames.Friendly("0"));
        Assert.Equal("Unknown", DoorStateNames.Friendly(null));

        // AllFriendlyNames covers the 7 runtime states.
        Assert.Equal(7, DoorStateNames.AllFriendlyNames.Count);
        Assert.Contains("Closed", DoorStateNames.AllFriendlyNames);
        Assert.Contains("Locked", DoorStateNames.AllFriendlyNames);
    }

    [Fact]
    public void DoorStateNames_ForwardCompat_ParsesUnknownEnumerators()
    {
        // A future game version may add E_DoorStates members. The UI keeps such
        // states selectable instead of clobbering them, which needs the index out.
        Assert.Equal(7, DoorStateNames.KnownStateCount);
        Assert.Equal(9, DoorStateNames.TryParseIndex("E_DoorStates::NewEnumerator9"));
        Assert.Equal(3, DoorStateNames.TryParseIndex("NewEnumerator3"));
        Assert.Equal(4, DoorStateNames.TryParseIndex("4"));
        Assert.Null(DoorStateNames.TryParseIndex("SomethingElse"));
        Assert.Null(DoorStateNames.TryParseIndex(null));

        // And the friendly label for an out-of-range state stays stable.
        Assert.Equal("State 9", DoorStateNames.Friendly("E_DoorStates::NewEnumerator9"));
    }

    [Fact]
    public void DoorIdParser_ExtractsMapAndActor()
    {
        var (map, actor) = DoorIdParser.Parse(
            "/Game/Maps/Facility.Facility:PersistentLevel.SimpleDoor_ParentBP_C_0");
        Assert.Equal("Facility", map);
        Assert.Equal("SimpleDoor_ParentBP_C_0", actor);

        var (map2, actor2) = DoorIdParser.Parse(
            "/Game/Maps/Facility_Containment.Facility_Containment:PersistentLevel.SecurityDoor_Animated_C_12");
        Assert.Equal("Facility_Containment", map2);
        Assert.Equal("SecurityDoor_Animated_C_12", actor2);

        // ClassNameFromActor strips the trailing _<n>.
        Assert.Equal("SimpleDoor_ParentBP_C", DoorIdParser.ClassNameFromActor("SimpleDoor_ParentBP_C_0"));
        Assert.Equal("StaticMeshActor", DoorIdParser.ClassNameFromActor("StaticMeshActor_1038"));

        // Non-conforming input: empty map, full id as actor.
        var (m3, a3) = DoorIdParser.Parse("weird_id_no_path");
        Assert.Equal(string.Empty, m3);
        Assert.Equal("weird_id_no_path", a3);
    }

    [Fact]
    public void DoorClassCatalog_LookupKnownAndUnknown()
    {
        var simple = DoorClassCatalog.Lookup("SimpleDoor_ParentBP_C");
        Assert.Equal("Hinged Door", simple.DisplayName);
        Assert.NotEqual("Unknown", simple.LockKind);

        // Security doors should land on a Keycard-style lock kind.
        var sec = DoorClassCatalog.Lookup("SecurityDoor_Animated_C");
        Assert.Equal("Keycard", sec.LockKind);

        // Unknown class: echo class name as display, lock = Unknown.
        var weird = DoorClassCatalog.Lookup("MysteryDoor_C");
        Assert.Equal("MysteryDoor_C", weird.DisplayName);
        Assert.Equal("Unknown", weird.LockKind);

        // KnownClasses index includes the parent BP families seen in saves.
        Assert.Contains("SimpleDoor_ParentBP_C", DoorClassCatalog.KnownClasses.Keys);
        Assert.Contains("BlastDoor_C", DoorClassCatalog.KnownClasses.Keys);
        Assert.Contains("VacDoor_BP_C", DoorClassCatalog.KnownClasses.Keys);
    }

    [Fact]
    public void QuestFlagCatalog_GroupsKnownFlags()
    {
        // Tutorial-ish.
        Assert.Equal(FlagCategory.Tutorial, QuestFlagCatalog.Lookup("Office_NewGameStarted").Category);
        Assert.Equal(FlagCategory.Tutorial, QuestFlagCatalog.Lookup("Office1TVTip").Category);

        // Quest progression.
        Assert.Equal(FlagCategory.Quest, QuestFlagCatalog.Lookup("Office_CafeteriaQuestStarted").Category);
        Assert.Equal(FlagCategory.Quest, QuestFlagCatalog.Lookup("Fog_Completed").Category);

        // Unlocks: doors / gates / pumps / map reveals.
        Assert.Equal(FlagCategory.Unlock, QuestFlagCatalog.Lookup("Office_CafeteriaUnlocked").Category);
        Assert.Equal(FlagCategory.Unlock, QuestFlagCatalog.Lookup("MapReveal_Labs").Category);
        Assert.Equal(FlagCategory.Unlock, QuestFlagCatalog.Lookup("Security_Gate1Opened").Category);
        Assert.Equal(FlagCategory.Unlock, QuestFlagCatalog.Lookup("MF_RedPumpFixed").Category);

        // NPC met.
        Assert.Equal(FlagCategory.Meta, QuestFlagCatalog.Lookup("LABS_MetAbe").Category);
        Assert.Equal(FlagCategory.Meta, QuestFlagCatalog.Lookup("MetChefTrader").Category);

        // Discovery / exploration.
        Assert.Equal(FlagCategory.Discovery, QuestFlagCatalog.Lookup("Office_ReachedLobby").Category);
        Assert.Equal(FlagCategory.Discovery, QuestFlagCatalog.Lookup("MFMines_Entered").Category);

        // FriendlyName composes area + humanised body.
        var info = QuestFlagCatalog.Lookup("Office_CafeteriaQuestStarted");
        Assert.Equal("Office", info.Area);
        Assert.Equal("Office: Cafeteria Quest Started", info.FriendlyName);

        // Mixed-case prefix normalised to a single canonical area.
        var labsLower = QuestFlagCatalog.Lookup("Labs_DiracGoal");
        var labsUpper = QuestFlagCatalog.Lookup("LABS_MetAbe");
        Assert.Equal("Labs", labsLower.Area);
        Assert.Equal("Labs", labsUpper.Area);

        // KnownAreas vocabulary is non-empty.
        Assert.NotEmpty(QuestFlagCatalog.KnownAreas);
        Assert.Contains("Office", QuestFlagCatalog.KnownAreas);
        Assert.Contains("Labs", QuestFlagCatalog.KnownAreas);
    }

    [Fact]
    public void QuestFlagCatalog_ClassifiesAllLiveSaveFlags()
    {
        // Spot-check against the actual flag list in our world-save fixture so
        // regressions in the heuristics surface immediately.
        Assert.NotNull(Fixtures.CascadeDir);
        if (!File.Exists(FacilitySavePath)) return;

        var save = WorldSaveReader.ReadFromFile(FacilitySavePath);

        var byCategory = save.Flags
            .Select(QuestFlagCatalog.Lookup)
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (cat, n) in byCategory.OrderByDescending(kv => kv.Value))
        {
            _output.WriteLine($"  {cat}: {n}");
        }

        // Every flag yields a non-null FriendlyName containing some letters.
        foreach (var f in save.Flags)
        {
            var info = QuestFlagCatalog.Lookup(f);
            Assert.False(string.IsNullOrWhiteSpace(info.FriendlyName), $"empty friendly name for {f}");
        }

        // We expect a meaningful spread - most flags should not collapse into
        // the catch-all Other bucket on the Facility save's ~50+ flags.
        var total = save.Flags.Count;
        var other = byCategory.TryGetValue(FlagCategory.Other, out var n2) ? n2 : 0;
        Assert.True(other * 2 < total,
            $"too many flags landed in Other ({other} of {total}); heuristics need tightening");
    }
}
