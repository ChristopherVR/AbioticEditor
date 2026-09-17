using System.IO;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Coverage assertions for <see cref="QuestFlagCatalog.KnownFlags"/> (moved out of the
/// AllFlagsProbe dump, which now lives in AbioticEditor.Probes): every flag observed in
/// the fixture saves must be enumerated, and the area grouping must stay total.
/// </summary>
public class QuestFlagCatalogCoverageTests
{
    private readonly ITestOutputHelper _output;

    public QuestFlagCatalogCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void KnownFlags_ContainsAllSeen()
    {
        // For every WorldSave_*.sav that actually carries flags, assert each
        // flag is enumerated in QuestFlagCatalog.KnownFlags.
        Assert.NotNull(Fixtures.CascadeDir);
        var savs = Directory.GetFiles(Fixtures.CascadeDir!, "WorldSave_*.sav")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var known = new HashSet<string>(QuestFlagCatalog.KnownFlags, StringComparer.Ordinal);
        var checkedNonEmpty = 0;
        foreach (var sav in savs)
        {
            var data = WorldSaveReader.ReadFromFile(sav);
            if (data.Flags.Count == 0) continue;
            checkedNonEmpty++;

            foreach (var f in data.Flags)
            {
                Assert.True(known.Contains(f),
                    $"flag '{f}' from {Path.GetFileName(sav)} missing from QuestFlagCatalog.KnownFlags");
            }
        }

        // Soft requirement: at least 1 in our current fixtures. If we ever get
        // 3+ populated saves this is the bar we'd want to enforce.
        Assert.True(checkedNonEmpty >= 1,
            $"expected at least one non-empty world save to verify against, got {checkedNonEmpty}");
        _output.WriteLine($"Verified {checkedNonEmpty} non-empty world save(s) against KnownFlags ({known.Count} entries).");
    }

    [Fact]
    public void FlagsByArea_GroupsAtLeastTenAreas()
    {
        Assert.True(QuestFlagCatalog.FlagsByArea.Count >= 10,
            $"expected at least 10 areas in FlagsByArea, got {QuestFlagCatalog.FlagsByArea.Count}");

        // Every area must have at least one flag.
        foreach (var (area, flags) in QuestFlagCatalog.FlagsByArea)
        {
            Assert.NotEmpty(flags);
            _output.WriteLine($"  {area}: {flags.Count}");
        }

        // And the union of all FlagsByArea values equals KnownFlags exactly.
        var fromAreas = QuestFlagCatalog.FlagsByArea
            .SelectMany(kv => kv.Value)
            .ToHashSet(StringComparer.Ordinal);
        var known = QuestFlagCatalog.KnownFlags.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(known.Count, fromAreas.Count);
        Assert.True(fromAreas.SetEquals(known),
            "FlagsByArea values do not match KnownFlags exactly");
    }

    /// <summary>
    /// The game's own flag table (tests/fixtures/world-flag-table.txt, from a running game) is
    /// the truth about which names are real flags. The offline flag screens list
    /// <see cref="QuestFlagCatalog.KnownFlags"/>, so every row the game has must be in it -
    /// 148 later-region flags (Reactors, Residence, Fracture, the ending) were missing before.
    /// </summary>
    [Fact]
    public void KnownFlags_covers_the_game_flag_table()
    {
        if (Fixtures.GameWorldFlags.Count == 0) return;
        var known = new HashSet<string>(QuestFlagCatalog.KnownFlags, StringComparer.Ordinal);
        var missing = Fixtures.GameWorldFlags.Where(flag => !known.Contains(flag)).ToList();
        Assert.True(missing.Count == 0, "flags the game has but KnownFlags lacks: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every name the curated prerequisite graph uses must be a real flag. A mistyped node
    /// ("Labs_Containment", which is only an area prefix; the flag is "Labs_Containment_Entered")
    /// once reached the running game through a chapter change and was rejected, which used to
    /// fail the whole edit.
    /// </summary>
    [Fact]
    public void Every_curated_prerequisite_names_a_real_flag()
    {
        if (Fixtures.GameWorldFlags.Count == 0) return;
        var real = new HashSet<string>(Fixtures.GameWorldFlags, StringComparer.OrdinalIgnoreCase);
        var unknown = QuestFlagDependencies.Direct
            .SelectMany(pair => pair.Value.Append(pair.Key))
            .Where(flag => !real.Contains(flag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(unknown.Count == 0, "curated prerequisite names that are not flags: " + string.Join(", ", unknown));
    }

    /// <summary>Every chapter trigger, and every flag a chapter move would set or clear, is a
    /// name the game actually has.</summary>
    [Fact]
    public void Every_chapter_plan_only_names_real_flags()
    {
        if (Fixtures.GameWorldFlags.Count == 0) return;
        var real = new HashSet<string>(Fixtures.GameWorldFlags, StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        foreach (var chapter in StoryProgressionCatalog.Chapters)
        {
            if (chapter.TriggerFlag is { } trigger && !real.Contains(trigger)) unknown.Add(chapter.Row + " trigger: " + trigger);
            var (flagsToSet, flagsToClear) = AbioticEditor.Web.Models.LiveStorySession.ComputeFlagPlan(chapter.Row, []);
            unknown.AddRange(flagsToSet.Concat(flagsToClear).Where(flag => !real.Contains(flag)).Select(flag => chapter.Row + ": " + flag));
        }
        Assert.True(unknown.Count == 0, "chapter plans naming flags the game does not have: " + string.Join(", ", unknown.Distinct()));
    }
}
