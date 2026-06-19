using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Locates an Abiotic Factor installation on the local machine. Currently looks up
/// the Steam install via the registry and walks libraryfolders.vdf to find
/// <c>AbioticFactor</c> under each configured library.
/// </summary>
public static class AfInstallLocator
{
    private const string GameFolderName = "AbioticFactor";

    /// <summary>
    /// Environment variable that points at the game install (any of the shapes
    /// <see cref="ResolvePaksDirectory"/> accepts). Lets CLI users and non-Steam installs
    /// (Game Pass, Epic, a moved library Steam can't enumerate) override auto-detection.
    /// </summary>
    public const string GameDirEnvVar = "ABIOTIC_GAME_DIR";

    /// <summary>
    /// Explicit install path set by the host (the App persists this from its Settings
    /// "Game Data" card). Wins over <see cref="GameDirEnvVar"/> and Steam auto-detection.
    /// Accepts any shape <see cref="ResolvePaksDirectory"/> understands; an unusable value
    /// is ignored and detection falls through rather than failing hard.
    /// </summary>
    public static string? OverrideInstallRoot { get; set; }

    /// <summary>
    /// Returns the absolute path to the game's <c>Content/Paks</c> directory, or null if
    /// the game can't be located. Resolution order: the explicit
    /// <see cref="OverrideInstallRoot"/> (in-process), then the <see cref="GameDirEnvVar"/>
    /// environment variable, then the persisted <see cref="GamePathStore"/> choice (so a folder
    /// picked in the app is honored by the CLI and by a freshly launched app, even though the CLI
    /// never sets <see cref="OverrideInstallRoot"/>), then Steam auto-detection. Each configured
    /// source is only used while it still resolves to paks, so a stale path falls through rather
    /// than disabling detection.
    /// </summary>
    public static string? FindPaksDirectory()
    {
        foreach (var configured in new[]
                 {
                     OverrideInstallRoot,
                     Environment.GetEnvironmentVariable(GameDirEnvVar),
                     GamePathStore.Saved,
                 })
        {
            if (!string.IsNullOrWhiteSpace(configured) && ResolvePaksDirectory(configured) is { } overridden)
            {
                return overridden;
            }
        }

        var root = FindInstallRoot();
        return root is null ? null : ResolvePaksDirectory(root);
    }

    /// <summary>
    /// Resolves a user-supplied or detected path to the game's <c>Content/Paks</c>
    /// directory, tolerating the shapes a player is likely to pick: the install root
    /// (contains <c>AbioticFactor/Content/Paks</c>), the inner <c>AbioticFactor</c> folder
    /// (contains <c>Content/Paks</c>), or the <c>Paks</c> folder itself. Returns null when
    /// none of those hold so a stray folder can't be mistaken for a valid install.
    /// </summary>
    public static string? ResolvePaksDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string[] structured =
        {
            Path.Combine(path, GameFolderName, "Content", "Paks"),
            Path.Combine(path, "Content", "Paks"),
        };
        foreach (var candidate in structured)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return LooksLikePaksDirectory(path) ? path : null;
    }

    /// <summary>True when <paramref name="dir"/> exists and holds at least one pak/utoc.</summary>
    private static bool LooksLikePaksDirectory(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                && (Directory.EnumerateFiles(dir, "*.pak").Any()
                    || Directory.EnumerateFiles(dir, "*.utoc").Any());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the absolute path to the AF install root (the directory containing
    /// <c>AbioticFactor/</c> and <c>Engine/</c>), or null if not found. Tries Steam first, then a
    /// Game Pass / Microsoft Store install.
    /// </summary>
    public static string? FindInstallRoot() => FindSteamInstallRoot() ?? FindGamePassInstallRoot();

    private static string? FindSteamInstallRoot()
    {
        var steam = FindSteamInstallPath();
        if (steam is null)
        {
            return null;
        }

        foreach (var library in EnumerateSteamLibraries(steam))
        {
            var candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Auto-detects a Game Pass / Microsoft Store install of Abiotic Factor. The Xbox app installs
    /// PC games to <c>&lt;drive&gt;:\XboxGames\&lt;Game Name&gt;\Content\</c> (on any fixed drive the
    /// user chose), where Content is the game's package root holding the UE
    /// <c>AbioticFactor/Content/Paks</c> tree. Returns the root <see cref="ResolvePaksDirectory"/>
    /// can resolve, or null. (The <c>C:\Program Files\WindowsApps</c> copy is ACL-locked and not
    /// readable without elevation, so it is not used.)
    /// </summary>
    public static string? FindGamePassInstallRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var drive in SafeFixedDrives())
        {
            var xboxGames = Path.Combine(drive, "XboxGames");
            foreach (var game in SafeDirectories(xboxGames))
            {
                if (!Path.GetFileName(game).Contains("Abiotic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (var root in new[] { Path.Combine(game, "Content"), game })
                {
                    if (ResolvePaksDirectory(root) is not null)
                    {
                        return root;
                    }
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> SafeFixedDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }
        foreach (var d in drives)
        {
            string? root = null;
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady) root = d.RootDirectory.FullName;
            }
            catch { /* ignore unreadable drive */ }
            if (root is not null) yield return root;
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>The Steam client install directory, or null when Steam isn't installed.</summary>
    public static string? FindSteamPath() => FindSteamInstallPath();

    /// <summary>
    /// Every configured Steam library root (the folders containing <c>steamapps/</c>),
    /// or empty when Steam isn't installed. Used by save discovery to find dedicated
    /// server installs in secondary libraries.
    /// </summary>
    public static IReadOnlyList<string> FindSteamLibraryRoots()
    {
        var steam = FindSteamInstallPath();
        return steam is null ? Array.Empty<string>() : EnumerateSteamLibraries(steam).ToList();
    }

    private static string? FindSteamInstallPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath")
            ?? ReadRegistry(RegistryHive.LocalMachine, RegistryView.Default, @"SOFTWARE\Valve\Steam", "InstallPath")
            ?? ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Valve\Steam", "SteamPath");
    }

    private static string? ReadRegistry(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamPath)
    {
        // The primary library always lives at <steamPath>/steamapps.
        yield return steamPath;

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            yield break;
        }

        string content;
        try
        {
            content = File.ReadAllText(vdfPath);
        }
        catch
        {
            yield break;
        }

        // The VDF format is line-oriented key/value pairs. The library paths appear as
        // `"path"  "C:\\Some\\Path"` entries inside numbered library blocks.
        var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
        foreach (Match m in matches)
        {
            var raw = m.Groups[1].Value;
            var normalized = raw.Replace("\\\\", "\\");
            if (!string.Equals(normalized, steamPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized;
            }
        }
    }
}
