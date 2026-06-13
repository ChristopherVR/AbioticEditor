using System.Reflection;

namespace AbioticEditor.Updater;

/// <summary>
/// The one type a host needs. Wraps the GitHub client, asset selection, and installer into
/// the two operations a host performs: <see cref="CheckForUpdateAsync"/> and
/// <see cref="DownloadAsync"/> + <see cref="ApplyAndExit"/>.
/// </summary>
/// <example>
/// <code>
/// var updater = new AppUpdater(UpdaterOptions.ForCli());
/// var result = await updater.CheckForUpdateAsync(currentVersion);
/// if (result.UpdateAvailable)
/// {
///     var staged = await updater.DownloadAsync(result);
///     updater.ApplyAndExit(staged);   // process exits; files swap; app relaunches
/// }
/// </code>
/// </example>
public sealed class AppUpdater
{
    private readonly UpdaterOptions _options;
    private readonly IUpdaterLog _log;
    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _client;
    private readonly UpdateInstaller _installer;

    public AppUpdater(UpdaterOptions options, IUpdaterLog? log = null, HttpClient? http = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? IUpdaterLog.Null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client = new GitHubReleaseClient(_options, _http);
        _installer = new UpdateInstaller(_options, _http, _log);
    }

    public UpdaterOptions Options => _options;

    /// <summary>True while the repo coordinates are still the unedited placeholders.</summary>
    public bool IsConfigured => !_options.IsPlaceholderRepository;

    /// <summary>
    /// Compares <paramref name="currentVersion"/> against the newest eligible release and
    /// selects the asset for this build. Never throws for the "nothing to do" cases - those
    /// are returned as a status. Network/API failures DO throw <see cref="UpdaterException"/>.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        string currentVersion, CancellationToken cancellationToken = default)
    {
        var release = await _client.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            _log.Info("No releases published yet.");
            return UpdateCheckResult.NoReleases(currentVersion);
        }

        var current = ReleaseVersion.TryParse(currentVersion);
        var latest = release.Version;

        // If either side is unparseable, fall back to "offer it only if the tag differs".
        var isNewer = current is not null && latest is not null
            ? current.IsOlderThan(latest)
            : !string.Equals(release.TagName, currentVersion, StringComparison.OrdinalIgnoreCase);

        if (!isNewer)
        {
            _log.Info($"Up to date ({currentVersion}; latest {release.TagName}).");
            return UpdateCheckResult.UpToDate(currentVersion, release);
        }

        var asset = AssetSelector.Select(release, _options.AssetKeywords);
        if (asset is null)
        {
            _log.Warn($"Release {release.TagName} has no asset matching "
                + $"[{string.Join(", ", _options.AssetKeywords)}].");
            return UpdateCheckResult.NoMatchingAsset(currentVersion, release);
        }

        _log.Info($"Update available: {release.TagName} (asset {asset.Name}).");
        return UpdateCheckResult.Available(currentVersion, release, asset);
    }

    /// <summary>Convenience overload reading the version from <paramref name="hostAssembly"/>.</summary>
    public Task<UpdateCheckResult> CheckForUpdateAsync(
        Assembly hostAssembly, CancellationToken cancellationToken = default)
        => CheckForUpdateAsync(AppVersionInfo.For(hostAssembly), cancellationToken);

    /// <summary>Downloads (and extracts) the asset from a successful check result.</summary>
    public Task<StagedUpdate> DownloadAsync(
        UpdateCheckResult result,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.UpdateAvailable || result.Asset is null || result.Release is null)
        {
            throw new UpdaterException("No update is available to download.");
        }
        return _installer.DownloadAsync(result.Asset, result.Release.TagName, progress, cancellationToken);
    }

    /// <summary>
    /// Applies a staged update and launches the swap. <b>The host must exit immediately
    /// after this returns</b> so the locked files can be replaced.
    /// </summary>
    public void ApplyAndExit(
        StagedUpdate staged,
        bool relaunch = true,
        string? installDirectory = null,
        string? relaunchPath = null)
    {
        UpdatePaths.CleanOldWorkingDirectories(keepTag: staged.Tag);
        _installer.ApplyAndExit(staged, relaunch, installDirectory, relaunchPath);
    }
}
