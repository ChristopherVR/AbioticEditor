namespace AbioticEditor.Updater;

/// <summary>The outcome of asking "is there a newer build than the one running?".</summary>
public sealed class UpdateCheckResult
{
    private UpdateCheckResult(
        UpdateCheckStatus status,
        string currentVersion,
        GitHubRelease? release,
        ReleaseAsset? asset,
        string? message)
    {
        Status = status;
        CurrentVersion = currentVersion;
        Release = release;
        Asset = asset;
        Message = message;
    }

    public UpdateCheckStatus Status { get; }

    /// <summary>The version string of the running build, as supplied by the host.</summary>
    public string CurrentVersion { get; }

    /// <summary>The newest eligible release (set for <see cref="UpdateCheckStatus.UpdateAvailable"/> and UpToDate).</summary>
    public GitHubRelease? Release { get; }

    /// <summary>The asset to download when an update is available; null otherwise.</summary>
    public ReleaseAsset? Asset { get; }

    /// <summary>The tag of the available release, or null.</summary>
    public string? LatestVersion => Release?.TagName;

    /// <summary>A human-readable explanation (why no update, or which version is offered).</summary>
    public string? Message { get; }

    /// <summary>True only when there is a newer release with a downloadable asset for this build.</summary>
    public bool UpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && Asset is not null;

    public static UpdateCheckResult Available(string current, GitHubRelease release, ReleaseAsset asset)
        => new(UpdateCheckStatus.UpdateAvailable, current, release, asset,
            $"{release.TagName} is available (you have {current}).");

    public static UpdateCheckResult UpToDate(string current, GitHubRelease? release)
        => new(UpdateCheckStatus.UpToDate, current, release, null,
            $"You are on the latest version ({current}).");

    public static UpdateCheckResult NoReleases(string current)
        => new(UpdateCheckStatus.NoReleases, current, null, null,
            "No releases have been published yet.");

    public static UpdateCheckResult NoMatchingAsset(string current, GitHubRelease release)
        => new(UpdateCheckStatus.NoMatchingAsset, current, release, null,
            $"{release.TagName} is published but has no downloadable asset for this build.");
}

public enum UpdateCheckStatus
{
    /// <summary>A newer release exists and ships an asset this build can install.</summary>
    UpdateAvailable,

    /// <summary>The running build is the latest (or newer than what is published).</summary>
    UpToDate,

    /// <summary>The repository exists but has published no eligible release.</summary>
    NoReleases,

    /// <summary>A newer release exists but carries no asset matching this build's keywords.</summary>
    NoMatchingAsset,
}
