namespace AbioticEditor.Core.Assets;

/// <summary>
/// Persists a user-chosen Abiotic Factor install location for when the automatic
/// Steam-registry discovery in <see cref="AfInstallLocator"/> can't find the game:
/// non-Steam copies (Game Pass, Epic), libraries Steam doesn't report, or saves edited on a
/// machine that doesn't have the game installed. The path lives next to the user mappings
/// override (under <c>%LOCALAPPDATA%/AbioticEditor</c>), so the CLI and the desktop app share
/// one configuration without either having to know about the other - the app writes it from
/// its Settings &gt; Game Data card and the CLI reads it during install resolution.
/// </summary>
public static class GamePathStore
{
    /// <summary>Where the chosen path is persisted (a single line of UTF-8 text).</summary>
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "gamepath.txt");

    /// <summary>The user-chosen install path, or null when none is set / the file is empty.</summary>
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

    /// <summary>Persists <paramref name="path"/> as the chosen install location.</summary>
    public static void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, path.Trim());
    }

    /// <summary>Removes any saved override so discovery falls back to Steam auto-detection.</summary>
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
