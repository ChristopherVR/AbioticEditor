using System.Runtime.CompilerServices;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Tests;

/// <summary>
/// Keeps a test run out of the installed editor's per-user folder.
/// </summary>
/// <remarks>
/// <para>Two things used to leak. <see cref="EditorLog.Error"/> writes even when logging is
/// switched off (by design: a real failure must never be silenced by the diagnostics toggle),
/// and it defaulted to <c>%LOCALAPPDATA%\AbioticEditor\logs</c>. The plugin-host tests
/// deliberately run a script whose handler throws, to prove the host survives it, so every
/// suite run appended that caught exception - stack trace and all - to the log file the
/// installed app shows the user. It read exactly like a plugin failing in the shipped app.</para>
/// <para>The same applied to <c>plugin-data</c>: throwaway test plugin ids were creating real
/// folders next to the user's genuinely installed plugin data.</para>
/// <para>This runs before any test, so the redirect is in place before Core's path statics are
/// first read. The folder is left behind for inspection and removed by a later run once it is a
/// few hours old (see <see cref="RemoveStaleTempFolders"/>).</para>
/// </remarks>
internal static class TestEnvironmentIsolation
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        RemoveStaleTempFolders();
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditor.Tests", $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        // Must be set before PluginPaths.AppDataRoot is first read (it is a static initializer).
        Environment.SetEnvironmentVariable("ABIOTIC_APPDATA_DIR", root);
        EditorLog.LogDirectory = Path.Combine(root, "logs");
    }

    /// <summary>
    /// Several tests copy a whole fixture world (about 65 MB) into the temp folder and delete it
    /// when done, but a run that is stopped part way, or a file still held open, leaves the copy
    /// behind, and the operating system does not clear the temp folder on its own. These piled up
    /// to gigabytes. Anything this suite left behind more than a few hours ago is removed before
    /// a new run starts; the age limit keeps a run that is going on at the same time (another
    /// checkout, say) untouched.
    /// </summary>
    private static void RemoveStaleTempFolders()
    {
        var temp = Path.GetTempPath();
        var cutoff = DateTime.UtcNow.AddHours(-3);
        var candidates = new List<string>();
        foreach (var pattern in new[] { "abiotic-containment-*", "abiotic-models-plugin-*" })
        {
            try { candidates.AddRange(Directory.EnumerateDirectories(temp, pattern)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var runs = Path.Combine(temp, "AbioticEditor.Tests");
        if (Directory.Exists(runs))
        {
            try { candidates.AddRange(Directory.EnumerateDirectories(runs, "run-*")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var folder in candidates)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(folder) < cutoff) Directory.Delete(folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
