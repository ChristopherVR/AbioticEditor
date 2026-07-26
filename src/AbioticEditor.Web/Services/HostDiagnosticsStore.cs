using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Remembers whether the player turned detailed logging on.
///
/// <para>The switch used to live only in memory, so it reset itself every time the editor
/// started. Someone chasing an intermittent problem would turn it on, restart to reproduce the
/// problem, and quietly be running with it off again. Every other preference (theme, language,
/// spoilers) is kept beside this one.</para>
/// </summary>
internal static class HostDiagnosticsStore
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "weblogging.txt");

    /// <summary>Applies the saved choice to <see cref="EditorLog"/>. Call once at startup.</summary>
    public static void Restore()
    {
        try
        {
            if (File.Exists(ConfigPath))
                EditorLog.Enabled = File.ReadAllText(ConfigPath).Trim() == "on";
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Save(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, enabled ? "on" : "off");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
