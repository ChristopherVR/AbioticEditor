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
    /// Somewhere other than a file to keep this in.
    /// </summary>
    /// <remarks>
    /// A browser has no lasting file system - what a WebAssembly app writes is thrown away when
    /// the tab reloads - so there the choice has to go into the browser's own storage instead.
    /// The host installs a pair of accessors at start-up; left unset, this stays the single text
    /// file the desktop app and the CLI have always shared.
    /// </remarks>
    public static void UseStore(Func<string?> read, Action<string?> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
    }

    private static Func<string?>? _read;
    private static Action<string?>? _write;

    /// <summary>
    /// The user-chosen game-data culture code (e.g. <c>"ru"</c>), or null when unset - callers
    /// then fall back to matching the editor's own UI language.
    /// </summary>
    public static string? Saved
    {
        get
        {
            if (_read is { } read) return Trimmed(read());
            try
            {
                return File.Exists(ConfigPath) ? Trimmed(File.ReadAllText(ConfigPath)) : null;
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
        if (_write is { } write) { write(culture.Trim()); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, culture.Trim());
    }

    /// <summary>Removes any saved override so the game-data language follows the UI language again.</summary>
    public static void Clear()
    {
        if (_write is { } write) { write(null); return; }
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

    private static string? Trimmed(string? text)
    {
        var value = text?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
