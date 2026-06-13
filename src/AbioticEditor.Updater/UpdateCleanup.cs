namespace AbioticEditor.Updater;

/// <summary>
/// Startup housekeeping for the in-place updater. The host calls <see cref="Run"/> early in
/// its startup (CLI <c>Main</c>, app constructor) to: finish any file swaps that were deferred
/// because the target was locked during the last update, and delete the <c>.old-update</c>
/// backups the previous version left behind. Cheap and silent when there is nothing to do.
/// </summary>
public static class UpdateCleanup
{
    /// <summary>Sweeps the directory the running build lives in.</summary>
    public static void Run(IUpdaterLog? log = null) => Run(UpdatePaths.CurrentInstallDirectory(), log);

    /// <summary>Finishes deferred swaps then removes stale backups under <paramref name="installDirectory"/>.</summary>
    public static void Run(string installDirectory, IUpdaterLog? log = null)
    {
        log ??= IUpdaterLog.Null;
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return;
        }

        try
        {
            ApplyPending(installDirectory, log);
            RemoveBackups(installDirectory, log);
        }
        catch (Exception ex)
        {
            // Cleanup must never stop the app from starting.
            log.Warn($"Update cleanup skipped: {ex.Message}");
        }
    }

    private static void ApplyPending(string root, IUpdaterLog log)
    {
        foreach (var pending in Directory.EnumerateFiles(
            root, "*" + InPlaceReplacer.PendingSuffix, SearchOption.AllDirectories))
        {
            var target = pending[..^InPlaceReplacer.PendingSuffix.Length];
            try
            {
                if (File.Exists(target))
                {
                    File.Move(target, target + InPlaceReplacer.OldSuffix, overwrite: true);
                }
                File.Move(pending, target, overwrite: true);
                log.Info($"Finished deferred update of '{Path.GetFileName(target)}'.");
            }
            catch (Exception ex)
            {
                // Still locked (rare): try again next start.
                log.Warn($"Pending update for '{Path.GetFileName(target)}' still deferred: {ex.Message}");
            }
        }
    }

    private static void RemoveBackups(string root, IUpdaterLog log)
    {
        foreach (var backup in Directory.EnumerateFiles(root, "*" + InPlaceReplacer.OldSuffix + "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(backup);
            }
            catch
            {
                // A backup that's somehow still in use just survives until the next run.
            }
        }
    }
}
