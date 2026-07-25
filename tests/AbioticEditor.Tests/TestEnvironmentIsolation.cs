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
/// first read. The folder is left behind for inspection; it is under the temp directory, so the
/// operating system reclaims it.</para>
/// </remarks>
internal static class TestEnvironmentIsolation
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditor.Tests", $"run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        // Must be set before PluginPaths.AppDataRoot is first read (it is a static initializer).
        Environment.SetEnvironmentVariable("ABIOTIC_APPDATA_DIR", root);
        EditorLog.LogDirectory = Path.Combine(root, "logs");
    }
}
