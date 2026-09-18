using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers <see cref="ProtonLiveAgentEnvironment"/>'s pure path-resolution logic against fake
/// <c>steamapps/common/.../compatdata/&lt;appid&gt;/pfx</c> fixture trees, the same style
/// <see cref="SaveDiscoveryTests"/> uses for <c>DiscoverProtonClientWorlds</c>. Does not (and
/// cannot, in this environment) exercise the actual Wine launch in
/// <c>AbioticEditor.Web.Services.LiveAgentSetup</c> - only the filesystem-only lookups this class
/// itself performs.
/// </summary>
public class ProtonLiveAgentEnvironmentTests
{
    private const int TestAppId = 427410;

    [Fact]
    public void FindSteamLibraryRoot_walks_up_from_the_inner_AbioticFactor_folder()
    {
        // <library>/steamapps/common/AbioticFactor/AbioticFactor/Binaries/Win64 - the double
        // nesting the game actually ships with (see GameInstallLocator's own remarks).
        var library = Path.Combine(Path.GetTempPath(), "abiotic-proton-test-lib");
        var binaries = Path.Combine(
            library, "steamapps", "common", "AbioticFactor", "AbioticFactor", "Binaries", "Win64");

        var found = ProtonLiveAgentEnvironment.FindSteamLibraryRoot(binaries);

        Assert.Equal(Path.GetFullPath(library), found);
    }

    [Fact]
    public void FindSteamLibraryRoot_walks_up_from_the_outer_install_root_too()
    {
        var library = Path.Combine(Path.GetTempPath(), "abiotic-proton-test-lib2");
        var installRoot = Path.Combine(library, "steamapps", "common", "AbioticFactor");

        var found = ProtonLiveAgentEnvironment.FindSteamLibraryRoot(installRoot);

        Assert.Equal(Path.GetFullPath(library), found);
    }

    [Fact]
    public void FindSteamLibraryRoot_returns_null_for_a_path_outside_any_steamapps_tree()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "some-random-folder", "AbioticFactor");

        Assert.Null(ProtonLiveAgentEnvironment.FindSteamLibraryRoot(customPath));
    }

    [Fact]
    public void FindPrefixRoot_returns_null_when_the_prefix_was_never_created()
    {
        using var tmp = new TempDir();

        Assert.Null(ProtonLiveAgentEnvironment.FindPrefixRoot(tmp.Path, TestAppId));
    }

    [Fact]
    public void FindPrefixRoot_finds_an_existing_compatdata_prefix()
    {
        using var tmp = new TempDir();
        var pfx = Path.Combine(tmp.Path, "steamapps", "compatdata", TestAppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "pfx");
        Directory.CreateDirectory(pfx);

        var found = ProtonLiveAgentEnvironment.FindPrefixRoot(tmp.Path, TestAppId);

        Assert.Equal(pfx, found);
    }

    [Fact]
    public void FindPrefixRoot_does_not_match_a_different_apps_prefix()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "steamapps", "compatdata", "12345", "pfx"));

        Assert.Null(ProtonLiveAgentEnvironment.FindPrefixRoot(tmp.Path, TestAppId));
    }

    [Fact]
    public void LocalAppDataIn_matches_the_layout_SaveDiscovery_already_relies_on()
    {
        var prefix = Path.Combine("some", "library", "steamapps", "compatdata", "427410", "pfx");

        var localAppData = ProtonLiveAgentEnvironment.LocalAppDataIn(prefix);

        Assert.Equal(
            Path.Combine(prefix, "drive_c", "users", "steamuser", "AppData", "Local"),
            localAppData);
    }

    [Fact]
    public void LocalAppDataCandidates_lists_the_expected_folder_first_then_every_prefix_user_in_both_layouts()
    {
        using var tmp = new TempDir();
        var prefix = Path.Combine(tmp.Path, "pfx");
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "users", "steamuser", "AppData", "Local"));
        // A plain Wine prefix (not Proton) names its user after the Linux account, and Wine 6 was
        // seen writing under the legacy layout - both must be probed, after the expected folder.
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "users", "christopher", "Local Settings", "Application Data"));

        var candidates = ProtonLiveAgentEnvironment.LocalAppDataCandidates(prefix).ToList();

        Assert.Equal(ProtonLiveAgentEnvironment.LocalAppDataIn(prefix), candidates[0]);
        Assert.Contains(Path.Combine(prefix, "drive_c", "users", "steamuser", "Local Settings", "Application Data"), candidates);
        Assert.Contains(Path.Combine(prefix, "drive_c", "users", "christopher", "AppData", "Local"), candidates);
        Assert.Contains(Path.Combine(prefix, "drive_c", "users", "christopher", "Local Settings", "Application Data"), candidates);
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void LocalAppDataCandidates_without_a_users_folder_still_yields_the_expected_one()
    {
        var prefix = Path.Combine("nowhere", "pfx");

        var candidates = ProtonLiveAgentEnvironment.LocalAppDataCandidates(prefix).ToList();

        Assert.Equal([ProtonLiveAgentEnvironment.LocalAppDataIn(prefix)], candidates);
    }

    [Fact]
    public void End_to_end_resolves_the_AbioticEditorLiveAgent_folder_from_an_install_path()
    {
        using var tmp = new TempDir();
        var installRoot = Path.Combine(tmp.Path, "steamapps", "common", "AbioticFactor");
        var pfx = Path.Combine(tmp.Path, "steamapps", "compatdata", TestAppId.ToString(System.Globalization.CultureInfo.InvariantCulture), "pfx");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(pfx);

        var library = ProtonLiveAgentEnvironment.FindSteamLibraryRoot(installRoot);
        Assert.NotNull(library);
        var prefixRoot = ProtonLiveAgentEnvironment.FindPrefixRoot(library!, TestAppId);
        Assert.NotNull(prefixRoot);
        var agentDir = Path.Combine(ProtonLiveAgentEnvironment.LocalAppDataIn(prefixRoot!), "AbioticEditorLiveAgent");

        Assert.Equal(
            Path.Combine(pfx, "drive_c", "users", "steamuser", "AppData", "Local", "AbioticEditorLiveAgent"),
            agentDir);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Directory.CreateDirectory(Path);

        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abiotic-proton-env-test-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
