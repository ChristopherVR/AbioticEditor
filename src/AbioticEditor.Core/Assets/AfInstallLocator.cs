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
    /// Returns the absolute path to <c>&lt;install&gt;/AbioticFactor/Content/Paks</c>,
    /// or null if the game can't be located.
    /// </summary>
    public static string? FindPaksDirectory()
    {
        var root = FindInstallRoot();
        if (root is null)
        {
            return null;
        }

        var paks = Path.Combine(root, "AbioticFactor", "Content", "Paks");
        return Directory.Exists(paks) ? paks : null;
    }

    /// <summary>
    /// Returns the absolute path to the AF install root (the directory containing
    /// <c>AbioticFactor/</c> and <c>Engine/</c>), or null if not found.
    /// </summary>
    public static string? FindInstallRoot()
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
