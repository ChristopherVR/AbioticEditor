using System.IO;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probe: walk every <c>WorldSave_*.sav</c> fixture, union their <c>Flags</c>
/// lists, and dump the sorted result to
/// <c>%TEMP%/abiotic-editor-schema/all-flags.txt</c>. The dump is the source
/// of truth for <see cref="QuestFlagCatalog.KnownFlags"/>.
///
/// The KnownFlags-coverage assertions that used to share this file are real tests and
/// live in <c>AbioticEditor.Tests/QuestFlagCatalogCoverageTests.cs</c>.
/// </summary>
public class AllFlagsProbe
{
    private readonly ITestOutputHelper _output;

    public AllFlagsProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DumpUnionOfAllWorldFlags()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var dir = Fixtures.CascadeDir!;

        var savs = Directory.GetFiles(dir, "WorldSave_*.sav")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _output.WriteLine($"=== Scanning {savs.Count} world saves in {dir} ===");

        var union = new SortedSet<string>(StringComparer.Ordinal);
        var perSave = new List<(string File, int Count)>();
        foreach (var sav in savs)
        {
            try
            {
                var data = WorldSaveReader.ReadFromFile(sav);
                foreach (var f in data.Flags) union.Add(f);
                perSave.Add((Path.GetFileName(sav), data.Flags.Count));
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {Path.GetFileName(sav)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine($"=== Union flag count: {union.Count} ===");
        _output.WriteLine("");

        // Per-area breakdown via the existing heuristics.
        var byArea = union
            .Select(QuestFlagCatalog.Lookup)
            .GroupBy(f => string.IsNullOrEmpty(f.Area) ? "(none)" : f.Area)
            .Select(g => (Area: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ToList();
        _output.WriteLine("=== Flags per area ===");
        foreach (var (area, count) in byArea)
        {
            _output.WriteLine($"  {area}: {count}");
        }
        _output.WriteLine("");

        _output.WriteLine("=== Per-save flag counts ===");
        foreach (var (file, count) in perSave.OrderByDescending(t => t.Count))
        {
            _output.WriteLine($"  {file}: {count}");
        }

        // Write the union to %TEMP%/abiotic-editor-schema/all-flags.txt.
        var outDir = Path.Combine(Path.GetTempPath(), "abiotic-editor-schema");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "all-flags.txt");
        File.WriteAllLines(outPath, union);
        _output.WriteLine("");
        _output.WriteLine($"=== Wrote {union.Count} flags to {outPath} ===");

        Assert.True(union.Count > 0, "expected at least one flag across all world saves");
    }
}
