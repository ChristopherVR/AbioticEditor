namespace AbioticEditor.Web.Services;

/// <summary>
/// Remembers the last app version a player has already been shown release notes for, so the
/// "what's new" dialog only ever appears once per version, the first time it launches after an
/// update - not on every launch. Mirrors <see cref="HostDiagnosticsStore"/>'s own shape (a single
/// plain-text value under <c>%LOCALAPPDATA%</c>, no config file format to keep in sync elsewhere).
/// </summary>
public static class ReleaseNotesStore
{
    /// <summary>Exposed (matching <c>ModLoadStore.DisabledModsPath</c>'s own precedent) so a test
    /// can back up and restore this real per-user file around itself instead of needing an
    /// injectable path this store has no other reason to support.</summary>
    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "releasenotesversion.txt");

    /// <summary>The version last recorded as shown, or null if nothing has been recorded yet
    /// (a brand-new install, or the file could not be read).</summary>
    public static string? LastShownVersion()
    {
        try
        {
            return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath).Trim() is { Length: > 0 } version ? version : null : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void MarkShown(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, version);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
