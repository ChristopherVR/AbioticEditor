namespace AbioticEditor.Core.Assets;

/// <summary>
/// Persists the user's chosen language for game-data text (item/trait/skill/recipe display
/// names, read from the game's own shipped translations) - deliberately separate from the
/// editor UI's own language, since the game ships a different culture set (e.g. <c>es-419</c>,
/// <c>ja</c>, <c>pt-BR</c>, <c>zh-Hans</c>) than the editor's UI translations do. Lives next to
/// <see cref="GamePathStore"/> so the CLI and the desktop app share one configuration.
/// </summary>
public static class GameDataLanguageStore
{
    /// <summary>Where the chosen culture is persisted (a single line of UTF-8 text).</summary>
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "gamedatalanguage.txt");

    /// <summary>
    /// The user-chosen game-data culture code (e.g. <c>"ru"</c>), or null when unset - callers
    /// then fall back to matching the editor's own UI language.
    /// </summary>
    public static string? Saved
    {
        get
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return null;
                }
                var text = File.ReadAllText(ConfigPath).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Persists <paramref name="culture"/> as the chosen game-data language.</summary>
    public static void Save(string culture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, culture.Trim());
    }

    /// <summary>Removes any saved override so the game-data language follows the UI language again.</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
        }
        catch
        {
            // Best-effort: a failed clear just leaves the override in place.
        }
    }
}
