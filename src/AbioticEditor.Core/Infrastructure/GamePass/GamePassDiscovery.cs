namespace AbioticEditor.Core.GamePass;

/// <summary>One discovered Game Pass save folder on this machine.</summary>
/// <param name="FolderPath">The wgs folder (contains containers.index).</param>
/// <param name="AccountId">The Xbox user id from the folder name (opaque).</param>
public sealed record DiscoveredGamePassSave(string FolderPath, string AccountId)
{
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Finds Abiotic Factor's Game Pass / Microsoft Store saves in the two places the Xbox "wgs"
/// (Connected Storage) layout uses on PC:
/// <list type="bullet">
///   <item>the packaged-app redirect
///     <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\SystemAppData\wgs\&lt;XUID&gt;_&lt;...&gt;\</c>
///     (any package whose name mentions Abiotic, since the publisher hash varies), and</item>
///   <item>the per-drive game-save store <c>&lt;drive&gt;:\XboxGames\GameSave\wgs\&lt;XUID&gt;_&lt;...&gt;\</c>
///     which is shared across titles, so each container is checked for the Abiotic package name.</item>
/// </list>
/// Never throws; inaccessible paths are skipped.
/// </summary>
public static class GamePassDiscovery
{
    public static IReadOnlyList<DiscoveredGamePassSave> DiscoverAll()
    {
        var results = new List<DiscoveredGamePassSave>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var roots = WgsRoots().ToList();
        Diagnostics.EditorLog.Info("GamePass",
            roots.Count == 0
                ? "Discovery: no wgs container-store roots found on this machine."
                : $"Discovery: scanning {roots.Count} wgs root(s): {string.Join(", ", roots)}");

        foreach (var wgs in roots)
        {
            foreach (var accountDir in SafeDirs(wgs))
            {
                if (!seen.Add(Path.GetFullPath(accountDir))) continue;

                // Log a verdict for every candidate so a remote dump shows exactly why a
                // Game Pass save was or wasn't picked up (the checks below are otherwise silent).
                var name = Path.GetFileName(accountDir);
                if (IgnoredFolderReason(name) is { } reason)
                {
                    Diagnostics.EditorLog.Info("GamePass", $"Discovery: '{name}' is {reason} - skipped.");
                    continue;
                }
                if (!WgsContainerStore.IsContainerFolder(accountDir))
                {
                    Diagnostics.EditorLog.Info("GamePass",
                        $"Discovery: '{name}' has no containers.index - skipped (not a container folder).");
                    continue;
                }
                if (!WgsContainerStore.IsAbioticContainerFolder(accountDir))
                {
                    Diagnostics.EditorLog.Info("GamePass",
                        $"Discovery: '{name}' has containers.index but is not an Abiotic Factor store - skipped.");
                    continue;
                }
                if (!IsAccountFolderName(name))
                {
                    // Accepted anyway: the layout is "<XUID>_<TitleScid>", but refusing everything
                    // that does not match would mean a save the editor can plainly read going
                    // missing because its folder was named in a way nobody here anticipated. Losing
                    // sight of a player's only save is worse than editing an unusual copy of one.
                    Diagnostics.EditorLog.Warn("GamePass",
                        $"Discovery: '{name}' holds an Abiotic save but is not named like an Xbox account "
                        + "folder. It may be a copy of one rather than the save the game uses.");
                }
                Diagnostics.EditorLog.Info("GamePass", $"Discovery: accepted Abiotic wgs folder '{name}'.");
                results.Add(new DiscoveredGamePassSave(
                    Path.GetFullPath(accountDir),
                    ParseAccountId(name))
                {
                    LastModified = LastWrite(accountDir),
                });
            }
        }
        Diagnostics.EditorLog.Info("GamePass", $"Discovery: {results.Count} Abiotic wgs folder(s) found.");
        return results;
    }

    /// <summary>
    /// The Game Pass container-store roots (the <c>wgs</c> folders) present on this machine, in
    /// scan order. Useful as a default parent when creating a new Game Pass world so it lands in
    /// the platform's own save area instead of the Steam tree. Empty when no Game Pass install is
    /// found; never throws.
    /// </summary>
    public static IReadOnlyList<string> ContainerStoreRoots()
        => WgsRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The wgs roots to scan for account folders, across both PC layouts and all drives.</summary>
    private static IEnumerable<string> WgsRoots()
    {
        // 1. The packaged-app redirect under %LOCALAPPDATA%\Packages\<Abiotic package>\SystemAppData\wgs.
        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        foreach (var package in SafeDirs(packages))
        {
            if (!Path.GetFileName(package).Contains("Abiotic", StringComparison.OrdinalIgnoreCase)) continue;
            var wgs = Path.Combine(package, "SystemAppData", "wgs");
            if (SafeExists(wgs)) yield return wgs;
        }

        // 2. The per-drive Xbox game-save store (shared across titles; filtered per container later).
        foreach (var drive in FixedDriveRoots())
        {
            var wgs = Path.Combine(drive, "XboxGames", "GameSave", "wgs");
            if (SafeExists(wgs)) yield return wgs;
        }
    }

    private static IEnumerable<string> FixedDriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }
        foreach (var d in drives)
        {
            string? root = null;
            try { if (d.DriveType == DriveType.Fixed && d.IsReady) root = d.RootDirectory.FullName; }
            catch { /* skip */ }
            if (root is not null) yield return root;
        }
    }

    /// <summary>
    /// Why a folder inside a <c>wgs</c> root is not a live save store, or null when it looks like
    /// one. Not everything in there is an account folder, and editing the wrong one is silent: the
    /// player's real save stays broken while the editor happily rewrites a copy of it.
    /// </summary>
    /// <remarks>
    /// <para>The <c>t</c> folder is Connected Storage's own scratch area. On a live Abiotic Factor
    /// install it sits next to the account folder, holds no <c>containers.index</c>, and was empty
    /// every time it was looked at. It is named here rather than left to the no-index check so the
    /// log says what it is instead of implying something is wrong with it.</para>
    /// <para>The rest are backup and copy shapes: this editor's own <c>.bak</c> snapshots (which
    /// stack up as <c>.bak-&lt;timestamp&gt;</c> and even <c>.bak.bak</c>), plus the naming other
    /// tools and the Xbox app are reported to leave behind.</para>
    /// </remarks>
    public static string? IgnoredFolderReason(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        if (string.Equals(folderName, "t", StringComparison.OrdinalIgnoreCase))
        {
            return "Xbox's own working folder, not a save";
        }
        if (folderName.StartsWith('~') || folderName.StartsWith('.'))
        {
            return "a hidden or temporary folder";
        }
        if (folderName.Contains(".bak", StringComparison.OrdinalIgnoreCase)
            || folderName.Contains("backup", StringComparison.OrdinalIgnoreCase))
        {
            return "a backup folder";
        }
        if (folderName.EndsWith(".old", StringComparison.OrdinalIgnoreCase)
            || folderName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || folderName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
        {
            return "a leftover folder";
        }
        if (folderName.Contains("copy", StringComparison.OrdinalIgnoreCase))
        {
            return "a copy of a save folder";
        }
        return null;
    }

    /// <summary>
    /// True when the folder is named the way Xbox names an account's save folder:
    /// <c>&lt;XUID&gt;_&lt;TitleScid&gt;</c>, both halves hexadecimal.
    /// </summary>
    public static bool IsAccountFolderName(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        var us = folderName.IndexOf('_', StringComparison.Ordinal);
        if (us < 8 || us == folderName.Length - 1) return false;
        return IsHex(folderName.AsSpan(0, us)) && IsHex(folderName.AsSpan(us + 1));
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    // wgs account folders are named "<XUID>_<TitleScid>"; the XUID is the part before the underscore.
    private static string ParseAccountId(string folderName)
    {
        var us = folderName.IndexOf('_');
        return us > 0 ? folderName[..us] : folderName;
    }

    private static bool SafeExists(string p)
    {
        try { return Directory.Exists(p); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<string> SafeDirs(string p)
    {
        try { return Directory.EnumerateDirectories(p); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static DateTime LastWrite(string dir)
    {
        try
        {
            var newest = DateTime.MinValue;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var t = File.GetLastWriteTime(f);
                if (t > newest) newest = t;
            }
            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
