using System.Runtime.InteropServices;

namespace AbioticEditor.Updater;

/// <summary>
/// Everything the updater needs to find, pick, and apply a release. Construct one per host
/// (the CLI and the app each build their own with the right <see cref="AssetKeywords"/>),
/// or start from <see cref="ForCli"/> / <see cref="ForApp"/> and tweak.
/// </summary>
public sealed class UpdaterOptions
{
    // ---------------------------------------------------------------------------------
    //  PLACEHOLDER - there is no GitHub repository yet. Change these two values to the
    //  real owner/name once the project is published, e.g. ("acme", "AbioticEditor").
    //  Everything else (release lookup, asset URLs) is derived from them.
    // ---------------------------------------------------------------------------------

    /// <summary>GitHub account or organisation that owns the releases repo. <b>CHANGE ME.</b></summary>
    public const string PlaceholderOwner = "YOUR_GITHUB_USERNAME";

    /// <summary>GitHub repository name that publishes the releases. <b>CHANGE ME.</b></summary>
    public const string PlaceholderRepository = "AbioticEditor";

    /// <summary>GitHub account or organisation that owns the releases repo.</summary>
    public string RepositoryOwner { get; set; } = PlaceholderOwner;

    /// <summary>GitHub repository name that publishes the releases.</summary>
    public string RepositoryName { get; set; } = PlaceholderRepository;

    /// <summary>
    /// Optional GitHub token. Only needed for a private repo or to lift the 60/hour
    /// unauthenticated rate limit; public releases work with no token.
    /// </summary>
    public string? GitHubToken { get; set; }

    /// <summary>When true, pre-release tags are eligible; otherwise only full releases.</summary>
    public bool AllowPrerelease { get; set; }

    /// <summary>
    /// Case-insensitive substrings that the chosen release asset's file name must ALL
    /// contain (e.g. <c>["cli", "win-x64"]</c>). This is how the CLI download is told
    /// apart from the app download in a release that ships both. When empty, the first
    /// archive/installer asset is used. See <see cref="DefaultPlatformKeyword"/>.
    /// </summary>
    public IReadOnlyList<string> AssetKeywords { get; set; } = Array.Empty<string>();

    /// <summary>Identifies the running build in logs and the User-Agent header.</summary>
    public string ProductName { get; set; } = "AbioticEditor";

    /// <summary>True when the repo coordinates are still the unedited placeholders.</summary>
    public bool IsPlaceholderRepository =>
        string.Equals(RepositoryOwner, PlaceholderOwner, StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(RepositoryOwner);

    /// <summary><c>https://api.github.com/repos/{owner}/{repo}</c> (no trailing slash).</summary>
    public string ApiBaseUrl => $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}";

    /// <summary>The OS/arch token GitHub assets are conventionally named with (win-x64, osx-arm64, linux-x64).</summary>
    public static string DefaultPlatformKeyword
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : "linux";
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64",
            };
            return $"{os}-{arch}";
        }
    }

    /// <summary>Options pre-set for the headless CLI package (asset must match "cli" + this OS/arch).</summary>
    public static UpdaterOptions ForCli() => new()
    {
        ProductName = "abioticeditor",
        AssetKeywords = new[] { "cli", DefaultPlatformKeyword },
    };

    /// <summary>Options pre-set for the desktop app package (asset must match "app" + this OS/arch).</summary>
    public static UpdaterOptions ForApp() => new()
    {
        ProductName = "AbioticEditor",
        AssetKeywords = new[] { "app", DefaultPlatformKeyword },
    };
}
