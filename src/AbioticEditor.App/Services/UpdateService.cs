using System.Reflection;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Updater;

namespace AbioticEditor.App.Services;

/// <summary>
/// The GUI's front door to the self-updater, kept static to match the app's other ambient
/// services (Theme/Spoiler/Plugins). Wraps <see cref="AppUpdater"/> with app-appropriate
/// options, bridges its diagnostics to <see cref="EditorLog"/>, and performs the
/// download → apply → relaunch → quit sequence the install button needs.
/// </summary>
public static class UpdateService
{
    private static readonly AppUpdater Updater = CreateUpdater();

    /// <summary>True until the GitHub repo coordinates in <see cref="UpdaterOptions"/> are filled in.</summary>
    public static bool IsPlaceholder => !Updater.IsConfigured;

    /// <summary>The running build's version, as shown in the ABOUT card.</summary>
    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    private static AppUpdater CreateUpdater()
    {
        var options = UpdaterOptions.ForApp();
        options.GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var log = IUpdaterLog.Delegating(
            info: m => EditorLog.Info("Update", m),
            warn: m => EditorLog.Warn("Update", m),
            error: (m, ex) => EditorLog.Error("Update", m, ex));
        return new AppUpdater(options, log);
    }

    private static string ResolveCurrentVersion()
    {
        // Prefer the MAUI display version (matches the ABOUT card); fall back to the assembly.
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch (Exception)
        {
            return AppVersionInfo.For(Assembly.GetExecutingAssembly());
        }
    }

    /// <summary>
    /// Finishes any update deferred from a previous run and clears stale backups. Call once at
    /// startup (App constructor), before anything else touches the install folder.
    /// </summary>
    public static void RunStartupCleanup()
        => UpdateCleanup.Run(IUpdaterLog.Delegating(
            info: m => EditorLog.Info("Update", m),
            warn: m => EditorLog.Warn("Update", m),
            error: (m, ex) => EditorLog.Error("Update", m, ex)));

    /// <summary>Checks GitHub for a newer release. Throws <see cref="UpdaterException"/> on network/API failure.</summary>
    public static Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => Updater.CheckForUpdateAsync(CurrentVersion, cancellationToken);

    /// <summary>
    /// Downloads the update from <paramref name="result"/>, applies it over the install in
    /// managed code, relaunches, and quits this app so the swapped files take effect.
    /// <paramref name="progress"/> reports 0..1 download fraction.
    /// </summary>
    public static async Task DownloadInstallAndRestartAsync(
        UpdateCheckResult result,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var staged = await Updater.DownloadAsync(result, progress, cancellationToken).ConfigureAwait(false);

        // Marshal back to the UI thread to apply + quit cleanly.
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Updater.ApplyAndExit(staged, relaunch: true);
            Application.Current?.Quit();
        }).ConfigureAwait(false);
    }
}
