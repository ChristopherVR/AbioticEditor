namespace AbioticEditor.Core.Assets;

/// <summary>
/// Persists whether the editor should mount Abiotic Factor mod paks (the
/// <c>~mods</c> and <c>LogicMods</c> subfolders of the game's <c>Content/Paks</c>).
/// Mods load by default; this lets a user fall back to a clean base-game view when a
/// mod's data confuses the editor. The flag lives next to the other shared settings
/// (<see cref="GamePathStore"/>, the user mappings override) under
/// <c>%LOCALAPPDATA%/AbioticEditor</c>, so the CLI and desktop app share one choice.
/// </summary>
public static class ModLoadStore
{
    /// <summary>
    /// Environment variable that force-disables mod loading regardless of the persisted
    /// flag (mirrors the plugin system's <c>ABIOTIC_NO_PLUGINS</c>). Set to <c>1</c>,
    /// <c>true</c>, or <c>yes</c> to disable.
    /// </summary>
    public const string DisableEnvVar = "ABIOTIC_NO_MODS";

    /// <summary>Where the flag is persisted (a single line: "1" enabled, "0" disabled).</summary>
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "mods-enabled.txt");

    /// <summary>
    /// The effective decision: true when mods should be mounted. False when the
    /// <see cref="DisableEnvVar"/> env var is set, otherwise the persisted flag
    /// (default true when no flag has been saved).
    /// </summary>
    public static bool ModsEnabled => !DisabledByEnv && PersistedEnabled;

    /// <summary>True when <see cref="DisableEnvVar"/> force-disables mod loading.</summary>
    public static bool DisabledByEnv
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(DisableEnvVar)?.Trim();
            return v is "1" or "true" or "True" or "yes" or "YES";
        }
    }

    /// <summary>The persisted flag alone (ignores the env override). Default true.</summary>
    public static bool PersistedEnabled
    {
        get
        {
            try
            {
                if (!File.Exists(ConfigPath)) return true;
                return File.ReadAllText(ConfigPath).Trim() != "0";
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>Persists the user's choice. The env override still wins over a saved "enabled".</summary>
    public static void SetPersistedEnabled(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, enabled ? "1" : "0");
    }
}
