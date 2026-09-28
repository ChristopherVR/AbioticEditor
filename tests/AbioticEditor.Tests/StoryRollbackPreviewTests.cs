using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Tests for the reviewed rewind consequence map and the read-only rollback preview built on it.
/// </summary>
public sealed class StoryRollbackPreviewTests(ITestOutputHelper output)
{
    private static readonly string[] PreviewChapters = ["Office", "Labs", "Reactors1Labs"];

    [Fact]
    public void Consequence_map_has_unique_ids_and_both_kinds()
    {
        var all = StoryRewindConsequenceCatalog.All;
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
        Assert.NotEmpty(StoryRewindConsequenceCatalog.Reversed);
        Assert.NotEmpty(StoryRewindConsequenceCatalog.NotReversed);
        Assert.All(all, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Detail));
            Assert.False(string.IsNullOrWhiteSpace(c.Evidence));
            Assert.DoesNotContain('\u2014', c.Detail + c.Title + c.Evidence);
        });

        // The four things the rewind really writes must be exactly the reversed set.
        Assert.Equal(
            "codex-player,codex-world,flags,respawn",
            string.Join(",", StoryRewindConsequenceCatalog.Reversed.Select(c => c.Id).Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("WorldSave_Facility_Pens.sav", "Pens")]
    [InlineData("Facility_Labs_Adjustment", "Labs")]
    [InlineData("WorldSave_Facility_Containment.sav", "Containment")]
    [InlineData("WorldSave_Facility_MFMines.sav", "MFMines")]
    [InlineData("WorldSave_Facility_DF_Central.sav", "ReactorsEntry")]
    [InlineData("WorldSave_Facility_Dam_Lower.sav", "EndSecurity")]
    [InlineData("WorldSave_Facility.sav", null)]
    [InlineData("WorldSave_Facility_Office1.sav", null)]
    [InlineData("WorldSave_V_Salem.sav", null)]
    public void Region_attribution_is_conservative(string token, string? expected)
        => Assert.Equal(expected, StoryRewindConsequenceCatalog.RegionOpensAtChapter(token));

    [Fact]
    public void Every_attributed_chapter_row_exists()
    {
        foreach (var (path, _) in AllFixtureSaves.WorldSaves)
        {
            if (StoryRewindConsequenceCatalog.RegionOpensAtChapter(path) is { } row)
            {
                Assert.True(StoryProgressionCatalog.IndexOf(row) >= 0, row);
            }
        }
    }

    [Fact]
    public void Preview_lists_only_regions_the_rewound_story_has_not_reached()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        foreach (var world in AllFixtureSaves.WorldSaves.GroupBy(w => Path.GetDirectoryName(w.Path)))
        {
            var regions = world.Select(w => (Path.GetFileName(w.Path), w.Data)).ToList();
            var facility = world.FirstOrDefault(w => Path.GetFileName(w.Path) == "WorldSave_Facility.sav").Data;

            foreach (var chapter in PreviewChapters)
            {
                var preview = StoryRollbackPreviewBuilder.Build(chapter, facility, regions);
                var target = StoryProgressionCatalog.IndexOf(chapter);
                Assert.All(preview.Residuals, r =>
                {
                    Assert.NotNull(r.OpensAtChapter);
                    Assert.True(StoryProgressionCatalog.IndexOf(r.OpensAtChapter) > target, r.Region);
                });
                Assert.All(preview.Unattributed, r => Assert.Null(r.OpensAtChapter));
                output.WriteLine($"{Path.GetFileName(world.Key)} -> {chapter}: flags={preview.FlagsCleared} residualTotal={preview.ResidualTotal} unattributedRows={preview.Unattributed.Count}");
            }

            // Rewinding "to" the final chapter leaves nothing beyond the target.
            Assert.False(StoryRollbackPreviewBuilder.Build("EndGame", facility, regions).HasResiduals);
        }
    }

    [Fact]
    public void Preview_earlier_target_never_finds_fewer_leftovers_than_a_later_one()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        foreach (var world in AllFixtureSaves.WorldSaves.GroupBy(w => Path.GetDirectoryName(w.Path)))
        {
            var regions = world.Select(w => (Path.GetFileName(w.Path), w.Data)).ToList();
            var early = StoryRollbackPreviewBuilder.Build("Office", null, regions).ResidualTotal;
            var late = StoryRollbackPreviewBuilder.Build("Reactors1Labs", null, regions).ResidualTotal;
            Assert.True(early >= late);
        }
    }

    [Fact]
    public void Preview_rejects_an_unknown_chapter()
        => Assert.Throws<ArgumentException>(() => StoryRollbackPreviewBuilder.Build("NoSuchChapter", null, []));

    [Fact]
    public void Preview_does_not_change_the_saves()
    {
        var pens = AllFixtureSaves.WorldSaves.FirstOrDefault(w => w.Path.EndsWith("WorldSave_Facility_Pens.sav", StringComparison.Ordinal));
        if (pens.Data is null) return;

        byte[] Bytes()
        {
            using var ms = new MemoryStream();
            pens.Data.Raw.WriteTo(ms);
            return ms.ToArray();
        }
        var before = Bytes();
        _ = StoryRollbackPreviewBuilder.Build("Office", null, [(Path.GetFileName(pens.Path), pens.Data)]);
        Assert.Equal(before, Bytes());
    }
}
