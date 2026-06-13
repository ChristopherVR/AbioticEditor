namespace AbioticEditor.Updater;

/// <summary>
/// Picks the single release asset this build should download. A release may carry several
/// files (CLI archive, app installer, source zips); the host narrows them with
/// <see cref="UpdaterOptions.AssetKeywords"/>.
/// </summary>
public static class AssetSelector
{
    /// <summary>File extensions the updater knows how to apply, best first.</summary>
    private static readonly string[] InstallableExtensions =
    {
        ".zip", ".msi", ".exe", ".pkg", ".dmg", ".tar.gz", ".tgz",
    };

    /// <summary>
    /// Returns the best matching asset, or null when nothing qualifies. An asset qualifies
    /// only if its name contains EVERY keyword (case-insensitive). Among the matches, an
    /// installable extension is preferred, then the larger file (the real package over a
    /// checksum/sig sidecar).
    /// </summary>
    public static ReleaseAsset? Select(GitHubRelease release, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(release);
        keywords ??= Array.Empty<string>();

        var candidates = release.Assets
            .Where(a => keywords.All(k =>
                a.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(a => IsInstallable(a.Name))
            .ThenBy(a => ExtensionRank(a.Name))
            .ThenByDescending(a => a.Size)
            .First();
    }

    /// <summary>True when the file looks like something <c>UpdateInstaller</c> can apply.</summary>
    public static bool IsInstallable(string fileName)
        => InstallableExtensions.Any(ext =>
            fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static int ExtensionRank(string fileName)
    {
        for (var i = 0; i < InstallableExtensions.Length; i++)
        {
            if (fileName.EndsWith(InstallableExtensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return int.MaxValue;
    }
}
