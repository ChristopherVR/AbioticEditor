using AbioticEditor.Updater;

namespace AbioticEditor.Web.Services;

/// <summary>Checks the release feed but intentionally leaves installation to the user on a hosted Razor app.</summary>
public sealed class HostUpdateService(ILogger<HostUpdateService> logger)
{
    private static readonly Action<ILogger, Exception?> LogFeedUnavailable = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1002, "ReleaseFeedUnavailable"),
        "The release feed could not be checked");

    public const string ReleasesUrl = "https://github.com/ChristopherVR/AbioticEditor/releases";

    public async Task<HostUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var options = UpdaterOptions.ForWeb();
        var updater = new AppUpdater(options);
        try
        {
            var result = await updater.CheckForUpdateAsync(typeof(HostUpdateService).Assembly, cancellationToken);
            return new HostUpdateStatus(
                result.UpdateAvailable ? "Updates_StateAvailable" : "Updates_StateCurrent",
                result.CurrentVersion,
                result.LatestVersion,
                result.UpdateAvailable ? "Updates_MessageAvailable" : "Updates_MessageCurrent",
                result.UpdateAvailable);
        }
        catch (UpdaterException exception)
        {
            LogFeedUnavailable(logger, exception);
            return new HostUpdateStatus("Updates_StateUnavailable", AppVersionInfo.For(typeof(HostUpdateService).Assembly), null,
                "Updates_MessageUnavailable", false);
        }
        catch (HttpRequestException exception)
        {
            LogFeedUnavailable(logger, exception);
            return new HostUpdateStatus("Updates_StateUnavailable", AppVersionInfo.For(typeof(HostUpdateService).Assembly), null,
                "Updates_MessageUnavailable", false);
        }
    }
}

public sealed record HostUpdateStatus(string StateResourceKey, string CurrentVersion, string? LatestVersion, string MessageResourceKey, bool UpdateAvailable);
