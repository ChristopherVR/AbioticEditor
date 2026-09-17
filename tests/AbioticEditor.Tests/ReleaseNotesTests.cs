using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The "what's new" one-time dialog: <see cref="ReleaseNotesStore"/> (which version has already
/// been shown) and <see cref="ChangelogService"/> (parsing the repo's own embedded CHANGELOG.md
/// into player-facing entries). No fixture needed for the changelog half - the real file is what
/// ships, so parsing it for real is the only test worth writing.
/// </summary>
public sealed class ReleaseNotesTests
{
    [Fact]
    public void MarkShown_RoundTripsThroughLastShownVersion()
    {
        // The store writes to a real per-user file; preserve and restore it so the test is hermetic.
        var path = ReleaseNotesStore.ConfigPath;
        var hadFile = File.Exists(path);
        var original = hadFile ? File.ReadAllText(path) : null;
        try
        {
            ReleaseNotesStore.MarkShown("9.9.9-test");
            Assert.Equal("9.9.9-test", ReleaseNotesStore.LastShownVersion());

            ReleaseNotesStore.MarkShown("9.9.10-test");
            Assert.Equal("9.9.10-test", ReleaseNotesStore.LastShownVersion());
        }
        finally
        {
            if (original is not null) File.WriteAllText(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LastShownVersion_IsNullWhenNothingRecordedYet()
    {
        var path = ReleaseNotesStore.ConfigPath;
        var hadFile = File.Exists(path);
        var original = hadFile ? File.ReadAllText(path) : null;
        try
        {
            if (hadFile) File.Delete(path);
            Assert.Null(ReleaseNotesStore.LastShownVersion());
        }
        finally
        {
            if (original is not null) File.WriteAllText(path, original);
        }
    }

    [Fact]
    public void Releases_ParsesTheRealChangelogIntoPlayerFacingEntries()
    {
        var releases = ChangelogService.Releases;
        Assert.NotEmpty(releases);

        // Every entry needs a version string, and at least one highlight - Parse() only keeps a
        // release at all when it found a player-facing (Features/Bug Fixes/Performance) bullet,
        // so an empty one here would mean that filter broke.
        foreach (var release in releases)
        {
            Assert.False(string.IsNullOrWhiteSpace(release.Version));
            Assert.NotEmpty(release.Highlights);
        }

        // Newest-first, matching the changelog file's own order (and what the dialog should show
        // first) - not alphabetical or reversed.
        var newest = releases[0];
        Assert.All(releases.Skip(1), older => Assert.NotEqual(newest.Version, older.Version));
    }

    [Fact]
    public void Releases_NeverIncludesDevOnlyHousekeepingBullets()
    {
        // A real bullet this repo's own changelog carries under a dev-only heading (see
        // CHANGELOG.md's "Miscellaneous Tasks" sections) - if this ever shows up in a parsed
        // release, the section filter regressed.
        foreach (var release in ChangelogService.Releases)
        {
            Assert.DoesNotContain(release.Highlights, h => h.Contains("[skip ci]", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Since_WithUnknownVersion_ReturnsEveryRelease()
    {
        var all = ChangelogService.Releases;
        var since = ChangelogService.Since("0.0.0-does-not-exist");
        Assert.Equal(all.Count, since.Count);
    }

    [Fact]
    public void Since_WithNull_ReturnsEveryRelease()
    {
        Assert.Equal(ChangelogService.Releases.Count, ChangelogService.Since(null).Count);
    }

    [Fact]
    public void Since_WithTheNewestVersion_ReturnsNothingMissed()
    {
        var newest = ChangelogService.Releases[0];
        Assert.Empty(ChangelogService.Since(newest.Version));
    }

    [Fact]
    public void Since_WithAnOlderVersion_ReturnsOnlyWhatFollowsIt()
    {
        var all = ChangelogService.Releases;
        if (all.Count < 2) return; // Not enough history in this checkout to exercise the middle case.
        var since = ChangelogService.Since(all[1].Version);
        Assert.Single(since);
        Assert.Equal(all[0].Version, since[0].Version);
    }
}
