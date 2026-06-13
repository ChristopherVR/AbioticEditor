namespace AbioticEditor.Updater;

/// <summary>
/// Replaces the installed files with staged ones <b>in pure managed code</b> - no batch /
/// shell / PowerShell script, and no elevation beyond ordinary write access to the install
/// folder.
///
/// <para>The trick that makes this possible: on Windows a running executable (and its loaded
/// DLLs) cannot be <i>overwritten or deleted</i>, but they <i>can be renamed</i>. So for each
/// file we move the in-use copy aside to a <c>.old-update</c> name (which always succeeds for
/// loaded images) and drop the new file into the original path. The current process keeps
/// running off its already-mapped image; the freshly written files are picked up by the
/// relaunched process. The leftover <c>.old-update</c> files are swept on next startup by
/// <see cref="UpdateCleanup"/>.</para>
///
/// <para>A file that genuinely can't be swapped (rare - an exclusively locked data file) is
/// written next to its target as <c>.pending-update</c> and finished at the next startup.</para>
/// </summary>
public static class InPlaceReplacer
{
    /// <summary>Suffix for the renamed-aside previous file (cleaned at next startup).</summary>
    public const string OldSuffix = ".old-update";

    /// <summary>Suffix for a new file that couldn't be swapped yet (applied at next startup).</summary>
    public const string PendingSuffix = ".pending-update";

    /// <summary>
    /// Copies everything under <paramref name="stagedDirectory"/> over
    /// <paramref name="installDirectory"/>. Returns the relative paths that had to be deferred
    /// to startup (empty on a fully clean apply).
    /// </summary>
    public static IReadOnlyList<string> Apply(
        string stagedDirectory, string installDirectory, IUpdaterLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        log ??= IUpdaterLog.Null;

        var deferred = new List<string>();
        var stagedRoot = Path.GetFullPath(stagedDirectory);
        var installRoot = Path.GetFullPath(installDirectory);
        Directory.CreateDirectory(installRoot);

        foreach (var source in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagedRoot, source);

            // Never let the updater clobber its own bookkeeping files.
            if (relative.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(PendingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(installRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (TryPlace(source, target, log))
            {
                continue;
            }

            // Couldn't swap now: leave the new bytes beside the target for startup to finish.
            try
            {
                File.Copy(source, target + PendingSuffix, overwrite: true);
                deferred.Add(relative);
                log.Warn($"Deferred '{relative}' to next startup (target is locked).");
            }
            catch (Exception ex)
            {
                log.Error($"Could not stage pending file for '{relative}'.", ex);
            }
        }

        return deferred;
    }

    /// <summary>
    /// Places one new file: a straight copy when the target is absent, otherwise rename the
    /// existing file aside (works on loaded images) and copy the new one in. Restores the
    /// original on failure so a half-applied file is never left behind.
    /// </summary>
    private static bool TryPlace(string source, string target, IUpdaterLog log)
    {
        if (!File.Exists(target))
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                log.Warn($"Direct copy of '{Path.GetFileName(target)}' failed: {ex.Message}");
                return false;
            }
        }

        var aside = UniqueAsidePath(target);
        try
        {
            File.Move(target, aside);
        }
        catch
        {
            // The in-use file refused even a rename - defer it.
            return false;
        }

        try
        {
            File.Copy(source, target, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // Put the original back so the running app stays intact.
            log.Warn($"Replacing '{Path.GetFileName(target)}' failed: {ex.Message}; restoring original.");
            try
            {
                File.Move(aside, target);
            }
            catch
            {
                // Original is still at 'aside'; startup cleanup will not delete a missing target's
                // backup, but the pending copy written by the caller covers recovery.
            }
            return false;
        }
    }

    private static string UniqueAsidePath(string target)
    {
        var candidate = target + OldSuffix;
        var n = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{target}{OldSuffix}.{n++}";
        }
        return candidate;
    }
}
