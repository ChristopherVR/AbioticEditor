#if !NEXUSMODS
using System.CommandLine;
using AbioticEditor.Updater;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>update</c> - checks GitHub Releases for a newer <c>abioticeditor</c> and, on
/// <c>update install</c>, downloads it and replaces the running binary in place. The bare
/// <c>update</c> verb just reports; nothing is downloaded without <c>install</c>.
/// </summary>
internal static class UpdateCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("update",
            "Check for and install a newer version of the tool from GitHub releases.");

        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit the check result as JSON (check only).",
        };
        var preOpt = new Option<bool>("--pre")
        {
            Description = "Include pre-release versions.",
        };

        // `update check` - report only.
        var check = new Command("check", "Report whether a newer version is available.");
        check.Options.Add(jsonOpt);
        check.Options.Add(preOpt);
        check.SetAction(parse => RunAsync(() => CheckAsync(
            parse.GetValue(quiet), parse.GetValue(jsonOpt), parse.GetValue(preOpt))));

        // `update install` - download + apply.
        var yesOpt = new Option<bool>("--yes", "-y")
        {
            Description = "Do not prompt for confirmation before installing.",
        };
        var relaunchOpt = new Option<bool>("--relaunch")
        {
            Description = "Relaunch the tool after updating (off by default for the CLI).",
        };
        var install = new Command("install", "Download and install the latest version, replacing this one.");
        install.Options.Add(preOpt);
        install.Options.Add(yesOpt);
        install.Options.Add(relaunchOpt);
        install.SetAction(parse => RunAsync(() => InstallAsync(
            parse.GetValue(quiet), parse.GetValue(preOpt),
            parse.GetValue(yesOpt), parse.GetValue(relaunchOpt))));

        cmd.Subcommands.Add(check);
        cmd.Subcommands.Add(install);

        // Bare `update` = check, with a hint on how to install.
        cmd.Options.Add(preOpt);
        cmd.SetAction(parse => RunAsync(() => CheckAsync(parse.GetValue(quiet), json: false, parse.GetValue(preOpt))));
        return cmd;
    }

    private static AppUpdater BuildUpdater(bool quiet, bool allowPrerelease)
    {
        var options = UpdaterOptions.ForCli();
        options.AllowPrerelease = allowPrerelease;
        options.GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return new AppUpdater(options, new ConsoleUpdaterLog(quiet));
    }

    private static string CurrentVersion()
        => AppVersionInfo.For(typeof(UpdateCommand).Assembly);

    private static async Task<int> CheckAsync(bool quiet, bool json, bool pre)
    {
        var updater = BuildUpdater(quiet, pre);
        var result = await updater.CheckForUpdateAsync(CurrentVersion()).ConfigureAwait(false);

        if (json)
        {
            Cli.WriteJson(new
            {
                status = result.Status.ToString(),
                currentVersion = result.CurrentVersion,
                latestVersion = result.LatestVersion,
                updateAvailable = result.UpdateAvailable,
                asset = result.Asset?.Name,
                releaseUrl = result.Release?.HtmlUrl,
                message = result.Message,
            });
            return Cli.Ok;
        }

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                Console.WriteLine($"Update available: {result.LatestVersion} (you have {result.CurrentVersion}).");
                Console.WriteLine($"  Asset:   {result.Asset!.Name}");
                if (!string.IsNullOrEmpty(result.Release?.HtmlUrl))
                {
                    Console.WriteLine($"  Release: {result.Release!.HtmlUrl}");
                }
                PrintNotes(result.Release?.Body, quiet);
                Console.WriteLine();
                Console.WriteLine("Run 'abioticeditor update install' to update.");
                break;
            case UpdateCheckStatus.UpToDate:
                Cli.Info(quiet, $"You are on the latest version ({result.CurrentVersion}).");
                break;
            case UpdateCheckStatus.NoReleases:
                Cli.Info(quiet, "No releases have been published yet.");
                break;
            case UpdateCheckStatus.NoMatchingAsset:
                Cli.Warn(result.Message ?? "The latest release has no asset for this platform.");
                break;
        }
        return Cli.Ok;
    }

    private static async Task<int> InstallAsync(bool quiet, bool pre, bool yes, bool relaunch)
    {
        var updater = BuildUpdater(quiet, pre);
        var result = await updater.CheckForUpdateAsync(CurrentVersion()).ConfigureAwait(false);

        if (!result.UpdateAvailable)
        {
            Cli.Info(quiet, result.Message ?? "No update available.");
            return Cli.Ok;
        }

        Console.WriteLine($"Update available: {result.LatestVersion} (you have {result.CurrentVersion}).");
        Console.WriteLine($"  Asset: {result.Asset!.Name}");

        if (!yes && !Confirm($"Download and install {result.LatestVersion} now?"))
        {
            Cli.Info(quiet, "Update cancelled.");
            return Cli.Ok;
        }

        Cli.Info(quiet, "Downloading...");
        var progress = quiet ? null : new ConsoleProgress();
        var staged = await updater.DownloadAsync(result, progress).ConfigureAwait(false);
        if (!quiet)
        {
            Console.WriteLine();
        }

        Console.WriteLine($"Installing {result.LatestVersion}. The tool will close to replace its files.");
        // Launches the detached swap script; this process must now exit so its files unlock.
        updater.ApplyAndExit(staged, relaunch);
        return Cli.Ok;
    }

    private static void PrintNotes(string? body, bool quiet)
    {
        if (quiet || string.IsNullOrWhiteSpace(body))
        {
            return;
        }
        Console.WriteLine();
        Console.WriteLine("Release notes:");
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N] ");
        var answer = Console.ReadLine()?.Trim();
        return answer is "y" or "Y" or "yes" or "YES";
    }

    /// <summary>
    /// Runs an async command body under the same exit-code contract as <see cref="Cli.Run"/>,
    /// translating updater failures into the user-error channel.
    /// </summary>
    private static int RunAsync(Func<Task<int>> body) => Cli.Run(() =>
    {
        try
        {
            return body().GetAwaiter().GetResult();
        }
        catch (UpdaterConfigurationException ex)
        {
            throw new CliUserErrorException(ex.Message);
        }
        catch (UpdaterException ex)
        {
            throw new CliUserErrorException(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw new CliUserErrorException($"could not reach GitHub: {ex.Message}");
        }
    });

    /// <summary>Bridges the updater's log to the console, honouring <c>--quiet</c>.</summary>
    private sealed class ConsoleUpdaterLog(bool quiet) : IUpdaterLog
    {
        public void Info(string message) => Cli.Info(quiet, message);

        public void Warn(string message) => Cli.Warn(message);

        public void Error(string message, Exception? ex = null)
            => Console.Error.WriteLine($"error: {message}{(ex is null ? string.Empty : $" ({ex.Message})")}");
    }

    /// <summary>A single-line console download progress indicator.</summary>
    private sealed class ConsoleProgress : IProgress<double>
    {
        private int _lastPercent = -1;

        public void Report(double value)
        {
            var percent = (int)(value * 100);
            if (percent == _lastPercent)
            {
                return;
            }
            _lastPercent = percent;
            Console.Write($"\r  {percent,3}%");
        }
    }
}
#endif
