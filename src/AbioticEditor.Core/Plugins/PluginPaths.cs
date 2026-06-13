namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Resolves the directories the host scans for plugins, and per-plugin data folders. Two
/// roots are searched so both shipped sample plugins and user-installed ones work out of
/// the box:
/// <list type="bullet">
/// <item><b>Bundled</b>: a <c>plugins</c> folder next to the running executable - for
///   plugins shipped alongside the app/CLI.</item>
/// <item><b>User</b>: <c>%LOCALAPPDATA%\AbioticEditor\plugins</c> - where users drop plugins
///   they install themselves; survives app updates.</item>
/// </list>
/// Each plugin lives in its own subfolder containing a <c>plugin.json</c> and its DLLs.
/// </summary>
public static class PluginPaths
{
    /// <summary>Folder name searched under each root and next to the executable.</summary>
    public const string FolderName = "plugins";

    /// <summary>
    /// <c>%LOCALAPPDATA%\AbioticEditor</c> - the app's per-user root, shared with logs and
    /// the usmap override. Created on demand by callers that write into it.
    /// </summary>
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor");

    /// <summary>User plugins root (<c>%LOCALAPPDATA%\AbioticEditor\plugins</c>).</summary>
    public static string UserPluginsDirectory { get; } = Path.Combine(AppDataRoot, FolderName);

    /// <summary>
    /// Bundled plugins root next to the executable (<c>&lt;exe dir&gt;\plugins</c>), or null
    /// when the base directory can't be determined.
    /// </summary>
    public static string? BundledPluginsDirectory
    {
        get
        {
            var baseDir = AppContext.BaseDirectory;
            return string.IsNullOrEmpty(baseDir) ? null : Path.Combine(baseDir, FolderName);
        }
    }

    /// <summary>
    /// The roots to scan, in priority order (user first, so a user-installed copy shadows a
    /// bundled one with the same id). Override via <c>ABIOTIC_PLUGINS_DIR</c> (semicolon- or
    /// <see cref="Path.PathSeparator"/>-separated) for tests and portable installs.
    /// </summary>
    public static IReadOnlyList<string> Roots()
    {
        var overridden = Environment.GetEnvironmentVariable("ABIOTIC_PLUGINS_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden
                .Split(new[] { Path.PathSeparator, ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        var roots = new List<string> { UserPluginsDirectory };
        if (BundledPluginsDirectory is { } bundled)
        {
            roots.Add(bundled);
        }
        return roots;
    }

    /// <summary>
    /// The writable data directory reserved for a plugin id
    /// (<c>%LOCALAPPDATA%\AbioticEditor\plugin-data\&lt;id&gt;</c>), created on first access.
    /// Kept separate from the plugin's install folder so user data is not lost when the
    /// plugin is reinstalled or updated.
    /// </summary>
    public static string DataDirectoryFor(string pluginId)
    {
        var safe = Sanitize(pluginId);
        var dir = Path.Combine(AppDataRoot, "plugin-data", safe);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Replaces path-hostile characters in an id so it is safe as a folder name.</summary>
    private static string Sanitize(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(id.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
