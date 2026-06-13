using AbioticEditor.Updater;

namespace AbioticEditor.Tests;

/// <summary>
/// Offline coverage of the self-updater's pure logic: version ordering, asset selection,
/// and the in-place file replace + startup cleanup. Network paths (GitHub API, download)
/// are not exercised here.
/// </summary>
public sealed class UpdaterTests
{
    // ---------- ReleaseVersion ----------

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("2", 2, 0, 0, null)]
    [InlineData("2.0.0-rc.1", 2, 0, 0, "rc.1")]
    [InlineData("1.4.2+sha.abcdef", 1, 4, 2, null)]
    public void ReleaseVersion_parses_leniently(string text, int major, int minor, int patch, string? pre)
    {
        var v = ReleaseVersion.TryParse(text);
        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.Prerelease);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public void ReleaseVersion_rejects_unparseable(string? text)
        => Assert.Null(ReleaseVersion.TryParse(text));

    [Fact]
    public void ReleaseVersion_orders_by_core_then_prerelease()
    {
        var a = ReleaseVersion.TryParse("1.0.0")!;
        var b = ReleaseVersion.TryParse("1.0.1")!;
        var rc = ReleaseVersion.TryParse("1.0.1-rc.1")!;

        Assert.True(a.IsOlderThan(b));
        // A pre-release of the same core is LOWER than the final release.
        Assert.True(rc.IsOlderThan(b));
        Assert.True(a.IsOlderThan(rc));
        Assert.True(a < b);
        Assert.True(b > rc);
        Assert.Equal(0, ReleaseVersion.TryParse("v1.0.0")!.CompareTo(a));
    }

    // ---------- AssetSelector ----------

    private static GitHubRelease ReleaseWith(params string[] assetNames)
        => new()
        {
            TagName = "v1.0.0",
            Assets = assetNames
                .Select(n => new ReleaseAsset { Name = n, DownloadUrl = "https://example/" + n, Size = n.Length })
                .ToArray(),
        };

    [Fact]
    public void AssetSelector_requires_all_keywords()
    {
        var release = ReleaseWith(
            "AbioticEditor-cli-win-x64.zip",
            "AbioticEditor-app-win-x64.zip",
            "AbioticEditor-cli-linux-x64.zip");

        var cli = AssetSelector.Select(release, new[] { "cli", "win-x64" });
        Assert.NotNull(cli);
        Assert.Equal("AbioticEditor-cli-win-x64.zip", cli!.Name);

        var app = AssetSelector.Select(release, new[] { "app", "win-x64" });
        Assert.Equal("AbioticEditor-app-win-x64.zip", app!.Name);
    }

    [Fact]
    public void AssetSelector_prefers_installable_over_sidecar()
    {
        var release = ReleaseWith(
            "AbioticEditor-cli-win-x64.zip.sha256",
            "AbioticEditor-cli-win-x64.zip");

        var chosen = AssetSelector.Select(release, new[] { "cli", "win-x64" });
        Assert.Equal("AbioticEditor-cli-win-x64.zip", chosen!.Name);
    }

    [Fact]
    public void AssetSelector_returns_null_when_nothing_matches()
        => Assert.Null(AssetSelector.Select(ReleaseWith("readme.txt"), new[] { "cli", "win-x64" }));

    // ---------- InPlaceReplacer + UpdateCleanup ----------

    [Fact]
    public void InPlaceReplacer_copies_new_and_replaces_existing()
    {
        using var temp = new TempDir();
        var staged = temp.Sub("staged");
        var install = temp.Sub("install");

        // Existing install: one file to overwrite, one untouched.
        File.WriteAllText(Path.Combine(install, "app.exe"), "OLD");
        File.WriteAllText(Path.Combine(install, "keep.txt"), "KEEP");

        // Staged update: replaces app.exe, adds a nested new file.
        File.WriteAllText(Path.Combine(staged, "app.exe"), "NEW");
        Directory.CreateDirectory(Path.Combine(staged, "data"));
        File.WriteAllText(Path.Combine(staged, "data", "added.bin"), "ADDED");

        var deferred = InPlaceReplacer.Apply(staged, install);

        Assert.Empty(deferred);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(install, "app.exe")));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(install, "keep.txt")));
        Assert.Equal("ADDED", File.ReadAllText(Path.Combine(install, "data", "added.bin")));
        // The replaced original was renamed aside, not deleted.
        Assert.True(File.Exists(Path.Combine(install, "app.exe" + InPlaceReplacer.OldSuffix)));
    }

    [Fact]
    public void UpdateCleanup_removes_backups_and_applies_pending()
    {
        using var temp = new TempDir();
        var install = temp.Sub("install");

        // Leftover backup from a prior apply.
        File.WriteAllText(Path.Combine(install, "old.dll" + InPlaceReplacer.OldSuffix), "stale");
        // A pending swap that was deferred because the target was locked last time.
        File.WriteAllText(Path.Combine(install, "locked.dll"), "OLD");
        File.WriteAllText(Path.Combine(install, "locked.dll" + InPlaceReplacer.PendingSuffix), "NEW");

        UpdateCleanup.Run(install);

        Assert.False(File.Exists(Path.Combine(install, "old.dll" + InPlaceReplacer.OldSuffix)));
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(install, "locked.dll")));
        Assert.False(File.Exists(Path.Combine(install, "locked.dll" + InPlaceReplacer.PendingSuffix)));
    }

    [Fact]
    public void Default_options_target_the_real_releases_repo()
    {
        // Defaults now point at the published repo, so the updater is configured.
        Assert.False(new UpdaterOptions().IsPlaceholderRepository);
        Assert.False(UpdaterOptions.ForCli().IsPlaceholderRepository);
        Assert.Equal(
            "https://api.github.com/repos/ChristopherVR/AbioticEditor",
            new UpdaterOptions().ApiBaseUrl);
    }

    [Fact]
    public void Blank_or_sentinel_owner_is_reported_as_unconfigured()
    {
        var unset = UpdaterOptions.ForApp();
        unset.RepositoryOwner = UpdaterOptions.PlaceholderOwner;
        Assert.True(unset.IsPlaceholderRepository);

        unset.RepositoryOwner = "   ";
        Assert.True(unset.IsPlaceholderRepository);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "abiotic-updater-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Sub(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
