namespace AbioticEditor.Updater;

/// <summary>
/// Where the updater stages downloads and scripts. Mirrors the rest of the app
/// (<c>%LOCALAPPDATA%\AbioticEditor\updates</c>) so cleanup and diagnostics know where to look.
/// </summary>
public static class UpdatePaths
{
    /// <summary><c>%LOCALAPPDATA%\AbioticEditor</c> (or the OS equivalent) - the app's per-user root.</summary>
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor");

    /// <summary>Root of all update working folders (<c>&lt;appdata&gt;\updates</c>).</summary>
    public static string UpdatesRoot { get; } = Path.Combine(AppDataRoot, "updates");

    /// <summary>The working folder for one release tag (download + staged extraction + script).</summary>
    public static string WorkingDirectoryFor(string tag)
    {
        var dir = Path.Combine(UpdatesRoot, Sanitize(tag));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The directory the running build lives in - the install target to overwrite.</summary>
    public static string CurrentInstallDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        return string.IsNullOrEmpty(baseDir)
            ? Directory.GetCurrentDirectory()
            : baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Deletes stale update working folders, keeping the one for <paramref name="keepTag"/>.</summary>
    public static void CleanOldWorkingDirectories(string? keepTag = null)
    {
        if (!Directory.Exists(UpdatesRoot))
        {
            return;
        }
        var keep = keepTag is null ? null : Sanitize(keepTag);
        foreach (var dir in Directory.GetDirectories(UpdatesRoot))
        {
            if (keep is not null
                && string.Equals(Path.GetFileName(dir), keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // A locked leftover folder is harmless; it will be retried next time.
            }
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
