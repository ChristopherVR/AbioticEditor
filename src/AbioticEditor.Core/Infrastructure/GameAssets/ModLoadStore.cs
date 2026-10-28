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
        AbioticEditor.Core.Plugins.PluginPaths.AppDataRoot, "mods-enabled.txt");

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

    // ---------- per-mod enable/disable ----------

    /// <summary>
    /// Where the set of individually-disabled mod names is persisted (one mod name per line). Sits
    /// alongside <see cref="ConfigPath"/>. A mod is enabled unless its name appears here, so doing
    /// nothing keeps every mod on - matching the master default.
    /// </summary>
    public static string DisabledModsPath { get; } = Path.Combine(
        AbioticEditor.Core.Plugins.PluginPaths.AppDataRoot, "mods-disabled.txt");

    /// <summary>The set of mod names the user has individually turned off (case-insensitive).</summary>
    public static IReadOnlySet<string> DisabledMods
    {
        get
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(DisabledModsPath))
                {
                    foreach (var line in File.ReadAllLines(DisabledModsPath))
                    {
                        var name = line.Trim();
                        if (name.Length > 0) set.Add(name);
                    }
                }
            }
            catch
            {
                // A bad/locked file just means "nothing individually disabled".
            }
            return set;
        }
    }

    /// <summary>
    /// True when the named mod should mount. Only reflects the per-mod choice; callers still gate on
    /// the master <see cref="ModsEnabled"/> for the effective decision.
    /// </summary>
    public static string EnabledOfficialModsPath { get; } = Path.Combine(
        AbioticEditor.Core.Plugins.PluginPaths.AppDataRoot, "mods-official-enabled.txt");

    /// <remarks>
    /// A mod.io mod the player has not chosen for in the editor follows the game's own
    /// <c>EnabledMods</c> setting, so the editor sees what the game loads. An explicit choice in
    /// the editor (on or off) then wins, and never changes the game's setting.
    /// </remarks>
    public static bool IsModEnabled(string modName)
    {
        if (!modName.StartsWith(ModIoInstallLocator.KeyPrefix, StringComparison.Ordinal)) return !DisabledMods.Contains(modName);
        try
        {
            if (File.Exists(EnabledOfficialModsPath) && File.ReadAllLines(EnabledOfficialModsPath).Contains(modName, StringComparer.OrdinalIgnoreCase)) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (DisabledMods.Contains(modName)) return false;
        return long.TryParse(modName.AsSpan(ModIoInstallLocator.KeyPrefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id)
            && ModIoInstallLocator.ReadGameEnabledMods()?.Contains(id) == true;
    }

    /// <summary>Turns one mod on/off and persists the change.</summary>
    public static void SetModEnabled(string modName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(modName)) return;
        if (modName.StartsWith(ModIoInstallLocator.KeyPrefix, StringComparison.Ordinal))
        {
            var selected = File.Exists(EnabledOfficialModsPath)
                ? File.ReadAllLines(EnabledOfficialModsPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            if (enabled) selected.Add(modName); else selected.Remove(modName);
            Directory.CreateDirectory(Path.GetDirectoryName(EnabledOfficialModsPath)!);
            File.WriteAllLines(EnabledOfficialModsPath, selected.Order(StringComparer.OrdinalIgnoreCase));
            // Remember an explicit "off" too, so it is not replaced by the game's own setting.
        }
        var set = new HashSet<string>(DisabledMods, StringComparer.OrdinalIgnoreCase);
        if (enabled) set.Remove(modName);
        else set.Add(modName);

        Directory.CreateDirectory(Path.GetDirectoryName(DisabledModsPath)!);
        File.WriteAllText(DisabledModsPath, string.Join(Environment.NewLine, set));
    }
}
