using System.IO;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// GitHub issue #59: the STORY tab of <c>WorldSave_MetaData.sav</c> named every chapter of the
/// main quest, all the way to the finale, even with spoiler protection on. Chapters the world has
/// not reached must count as future content, judged from what the save itself says.
/// </summary>
public class StorySpoilerTests
{
    private static Func<string, bool> Flags(params string[] set)
    {
        var known = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        return known.Contains;
    }

    private static int Index(string row) => StoryProgressionCatalog.IndexOf(row);

    [Fact]
    public void A_fresh_world_only_shows_the_opening_chapter()
    {
        Assert.False(StorySpoilers.IsFutureChapter(0, null, Flags()));
        for (var i = 1; i < StoryProgressionCatalog.Chapters.Count; i++)
            Assert.True(StorySpoilers.IsFutureChapter(i, null, Flags()), StoryProgressionCatalog.Chapters[i].Row);
    }

    [Fact]
    public void The_stored_chapter_and_everything_before_it_are_reached()
    {
        var none = Flags();
        Assert.False(StorySpoilers.IsFutureChapter(Index("Labs"), "Labs", none));
        Assert.False(StorySpoilers.IsFutureChapter(Index("MF"), "Labs", none));
        Assert.True(StorySpoilers.IsFutureChapter(Index("Containment"), "Labs", none));
        Assert.True(StorySpoilers.IsFutureChapter(Index("EndGame"), "Labs", none));
    }

    [Fact]
    public void A_later_trigger_flag_wins_over_an_older_stored_chapter()
    {
        // The pointer can lag behind the world's flags; a later trigger implies the earlier ones.
        var flags = Flags("Security_SurfaceElevatorEvent");
        Assert.False(StorySpoilers.IsFutureChapter(Index("Labs"), "Office", flags));
        Assert.False(StorySpoilers.IsFutureChapter(Index("SecSurfaceElevator"), "Office", flags));
        Assert.True(StorySpoilers.IsFutureChapter(Index("EndSecurity"), "Office", flags));
    }

    [Fact]
    public void Out_of_range_indexes_are_never_hidden()
    {
        Assert.False(StorySpoilers.IsFutureChapter(-1, null, Flags()));
        Assert.False(StorySpoilers.IsFutureChapter(StoryProgressionCatalog.Chapters.Count, null, Flags()));
    }

    [Fact]
    public void Real_mid_story_save_hides_the_chapters_after_its_own()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        if (!File.Exists(path)) return;

        var meta = WorldSaveReader.ReadFromFile(path);
        var current = StoryProgressionCatalog.IndexOf(meta.StoryProgressionRow);
        Assert.True(current > 0);
        var facilityPath = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        var flags = File.Exists(facilityPath)
            ? new HashSet<string>(WorldSaveReader.ReadFromFile(facilityPath).Flags, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reached = StorySpoilers.FurthestReachedIndex(meta.StoryProgressionRow, flags.Contains);
        Assert.True(reached >= current);

        for (var i = 0; i < StoryProgressionCatalog.Chapters.Count; i++)
            Assert.Equal(i > reached, StorySpoilers.IsFutureChapter(i, meta.StoryProgressionRow, flags.Contains));
        // The finale is never shown to a world that has not got there.
        if (reached < StoryProgressionCatalog.Chapters.Count - 1)
            Assert.True(StorySpoilers.IsFutureChapter(Index("EndGame"), meta.StoryProgressionRow, flags.Contains));
    }

    [Fact]
    public void Real_completed_save_hides_nothing()
    {
        if (Fixtures.ServerWorldsDir is null) return;
        var metaPath = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_MetaData.sav");
        var facilityPath = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_Facility.sav");
        if (!File.Exists(metaPath) || !File.Exists(facilityPath)) return;

        var meta = WorldSaveReader.ReadFromFile(metaPath);
        var flags = new HashSet<string>(WorldSaveReader.ReadFromFile(facilityPath).Flags, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < StoryProgressionCatalog.Chapters.Count; i++)
            Assert.False(StorySpoilers.IsFutureChapter(i, meta.StoryProgressionRow, flags.Contains), StoryProgressionCatalog.Chapters[i].Row);
    }

    [Fact]
    public void Story_tab_applies_the_spoiler_guard_to_chapters_recipes_and_seen_lists()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldStoryTab.razor");
        Assert.Contains("@inject HostSpoilerPreferences Spoilers", source, StringComparison.Ordinal);
        Assert.Contains("StorySpoilers.IsFutureChapter", source, StringComparison.Ordinal);
        Assert.Contains("ShownChapterTitle(chapter)", source, StringComparison.Ordinal);
        Assert.Contains("ShownRecipeName(row)", source, StringComparison.Ordinal);
        Assert.Contains("ShownGlobalName(row)", source, StringComparison.Ordinal);
        Assert.Contains("Spoiler_OverrideTitle", source, StringComparison.Ordinal);
    }
}
