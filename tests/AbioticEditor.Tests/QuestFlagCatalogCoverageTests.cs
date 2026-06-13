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
}
