namespace AbioticEditor.Updater;

/// <summary>One downloadable file attached to a GitHub release.</summary>
public sealed class ReleaseAsset
{
    public required string Name { get; init; }

    /// <summary>The <c>browser_download_url</c> - a direct, redirecting download link.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Size in bytes (0 when GitHub omitted it).</summary>
    public long Size { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// The slice of a GitHub release the updater cares about. Built from the JSON returned by
/// <c>GET /repos/{owner}/{repo}/releases/latest</c> (or the releases list).
/// </summary>
public sealed class GitHubRelease
{
    /// <summary>The Git tag the release points at (e.g. <c>v1.4.2</c>).</summary>
    public required string TagName { get; init; }

    /// <summary>Human title of the release; falls back to the tag when GitHub left it blank.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Markdown release notes / changelog body.</summary>
    public string Body { get; init; } = string.Empty;

    public bool Prerelease { get; init; }

    public bool Draft { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>The release page on github.com.</summary>
    public string HtmlUrl { get; init; } = string.Empty;

    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = Array.Empty<ReleaseAsset>();

    /// <summary>The parsed <see cref="ReleaseVersion"/> of <see cref="TagName"/>, or null if unparseable.</summary>
    public ReleaseVersion? Version => ReleaseVersion.TryParse(TagName);
}
