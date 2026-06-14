namespace AbioticEditor.Core.Saves;

/// <summary>
/// Pre-write backups and crash-safe writes. Every editor write first copies the existing file
/// to <c>&lt;name&gt;.sav.bak</c> (overwriting any earlier backup), then writes via a temp file
/// and an atomic replace so a failure mid-serialize can never truncate or corrupt the live save.
/// One backup level deep is enough to recover from a bad write while never growing unbounded.
/// </summary>
public static class SaveBackup
{
    /// <summary>
    /// Copies <paramref name="path"/> to <c>&lt;path&gt;.bak</c> (overwriting any earlier backup).
    /// A failed backup must not block the save itself, so this never throws; the return value lets
    /// callers report when no recoverable copy was made.
    /// </summary>
    /// <returns>
    /// True when a backup exists afterward, or the source did not exist (nothing to back up);
    /// false when the copy was attempted and failed.
    /// </returns>
    public static bool CreateFor(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            return true;
        }
        catch
        {
            // A failed backup must not block the save itself.
            return false;
        }
    }

    /// <summary>
    /// Backs up <paramref name="path"/> (best-effort) then writes new contents through
    /// <paramref name="write"/> into a sibling temp file and atomically replaces the target. If
    /// <paramref name="write"/> throws partway, the temp file is discarded and the live save is
    /// left exactly as it was - so a serialization failure can never leave a truncated/corrupt
    /// save behind, even when the backup could not be made.
    /// </summary>
    public static void WriteWithBackup(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);

        CreateFor(path);

        // Temp lives in the same directory so the replace is a same-volume atomic rename.
        var temp = path + ".tmp";
        try
        {
            using (var fs = File.Create(temp))
            {
                write(fs);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the temp file; nothing actionable if it can't be removed.
        }
    }
}
