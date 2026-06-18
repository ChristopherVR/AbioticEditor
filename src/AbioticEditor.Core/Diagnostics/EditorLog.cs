namespace AbioticEditor.Core.Diagnostics;

/// <summary>
/// Opt-in diagnostic logging for the editor. Disabled by default; when enabled, every
/// call appends a line to <c>%LOCALAPPDATA%\AbioticEditor\logs\editor-YYYYMMDD.log</c>.
/// One file per day, at most <see cref="MaxLogFiles"/> files kept (older ones deleted).
///
/// <para><see cref="Error"/> is the exception: errors are CRITICAL and always written,
/// even when <see cref="Enabled"/> is false, so a failure is never silent just because a
/// user left diagnostics off. <see cref="Info"/>/<see cref="Warn"/>/<see cref="UnknownData"/>
/// stay gated behind <see cref="Enabled"/>.</para>
///
/// The logger must never take the app down: all of its own IO errors are swallowed.
/// Writes are line-buffered (open/append/flush/close per line) so a crash loses at most
/// the line being written, and the file is always readable while the app runs.
/// </summary>
public static class EditorLog
{
    public const int MaxLogFiles = 7;

    private static readonly object Sync = new();

    private static string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "logs");

    /// <summary>Master switch. Default false - no files are touched until enabled.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Where log files live. Settable for tests; defaults to
    /// <c>%LOCALAPPDATA%\AbioticEditor\logs</c>.
    /// </summary>
    public static string LogDirectory
    {
        get => _directory;
        set
        {
            lock (Sync)
            {
                _directory = value;
            }
        }
    }

    /// <summary>Today's log file path (the file may not exist yet).</summary>
    public static string CurrentLogFilePath
        => Path.Combine(_directory, $"editor-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string area, string message, Exception? ex = null)
        => Write("INFO ", area, message, ex);

    public static void Warn(string area, string message, Exception? ex = null)
        => Write("WARN ", area, message, ex);

    /// <summary>Logs a critical failure. Always written, even when <see cref="Enabled"/>
    /// is false - an error must never be silenced by the diagnostics toggle.</summary>
    public static void Error(string area, string message, Exception? ex = null)
        => Write("ERROR", area, message, ex, force: true);

    // ---------- unknown-data channel ----------

    private static readonly HashSet<string> SeenUnknown = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised for every <see cref="UnknownData"/> call, BEFORE the <see cref="Enabled"/>
    /// gate and the file-log dedup - so in-memory observers (the compatibility report's
    /// <c>UnknownContentCollector</c>) work even with logging off. Observers receive
    /// every occurrence and must dedup themselves if they care.
    /// </summary>
    internal static event Action<string, string, string?>? UnknownDataObserved;

    /// <summary>
    /// Logs data the editor has NO model for (unconsumed save properties, unknown
    /// classes/enums/rows) - the things a user can't see in the UI but may need when a
    /// game update changes the format. Deduplicated per session per (area, key) so a
    /// 400-container save doesn't write the same unknown key 400 times.
    /// </summary>
    public static void UnknownData(string area, string key, string? context = null)
    {
        UnknownDataObserved?.Invoke(area, key, context);

        if (!Enabled) return;
        lock (Sync)
        {
            if (!SeenUnknown.Add($"{area}|{key}")) return;
        }
        Write("UNKWN", area, context is null ? key : $"{key} ({context})", null);
    }

    /// <summary>Resets the unknown-data dedup (a fresh file load is a fresh inventory).</summary>
    public static void ResetUnknownDedup()
    {
        lock (Sync)
        {
            SeenUnknown.Clear();
        }
    }

    private static void Write(string level, string area, string message, Exception? ex, bool force = false)
    {
        // force bypasses the master switch for critical (error) logs only.
        if (!Enabled && !force) return;

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(_directory);
                var path = CurrentLogFilePath;
                var isNewFile = !File.Exists(path);

                using (var writer = File.AppendText(path))
                {
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {area}: {message}");
                    if (ex is not null)
                    {
                        // Indent the exception so a log line always starts with a timestamp.
                        writer.WriteLine("    " + ex.ToString().Replace("\n", "\n    "));
                    }
                }

                if (isNewFile)
                {
                    PruneOldLogs();
                }
            }
        }
        catch
        {
            // Diagnostics must never break the feature being diagnosed.
        }
    }

    /// <summary>Deletes the oldest <c>editor-*.log</c> files beyond <see cref="MaxLogFiles"/>.</summary>
    private static void PruneOldLogs()
    {
        // File names embed the date (editor-YYYYMMDD.log), so ordinal name order is
        // chronological - no need to trust file timestamps.
        var files = Directory.GetFiles(_directory, "editor-*.log")
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = MaxLogFiles; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch
            {
                // A locked old log just survives one more day.
            }
        }
    }
}
