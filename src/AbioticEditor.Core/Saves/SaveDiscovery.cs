using AbioticEditor.Core.Assets;

namespace AbioticEditor.Core.Saves;

/// <summary>Where a discovered world's saves come from.</summary>
public enum DiscoveredWorldSource
{
    /// <summary>The game client's local saves under %LOCALAPPDATA%\AbioticFactor.</summary>
    Client,

    /// <summary>A dedicated-server install found in a Steam library.</summary>
    DedicatedServer,
}

/// <summary>One world folder found on this machine, loadable by the editor.</summary>
/// <param name="FolderPath">The world folder (contains WorldSave_*.sav and PlayerData/).</param>
/// <param name="WorldName">The world's folder name, e.g. "Cascade".</param>
/// <param name="Source">Client saves or a dedicated-server install.</param>
/// <param name="AccountId">The owning account folder name for client saves (a steamid64 on
/// Steam, an opaque platform id on Game Pass / non-Steam copies); null for servers.</param>
public sealed record DiscoveredWorld(
    string FolderPath,
    string WorldName,
    DiscoveredWorldSource Source,
    string? AccountId)
{
    public string SourceLabel => Source == DiscoveredWorldSource.Client ? "CLIENT" : "SERVER";

    /// <summary>Number of .sav files directly in the world folder tree (cheap signal of activity).</summary>
    public int SaveFileCount { get; init; }

    /// <summary>Newest .sav write time in the world folder; effectively "last played".</summary>
    public DateTime LastPlayed { get; init; }
}

/// <summary>
/// Finds every Abiotic Factor world installed on this machine, so the editor can offer
/// them on startup instead of making the user hunt for folders. Three layouts exist:
/// Steam/standalone client saves under <c>%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\&lt;account&gt;\Worlds\&lt;World&gt;</c>,
/// Microsoft Store / Game Pass client saves under the packaged-app redirect
/// <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\...\AbioticFactor\Saved\SaveGames\&lt;account&gt;\Worlds</c>,
/// and dedicated-server installs in Steam libraries, which keep their saves inside the
/// install folder (a <c>Worlds\&lt;World&gt;</c> tree under any <c>SaveGames</c> directory).
/// The per-account folder is treated as an opaque id, so non-Steam (non-numeric) owners
/// list and load like any other.
/// </summary>
public static class SaveDiscovery
{
    /// <summary>
    /// Scans the machine: the client save tree plus every Steam library's
    /// Abiotic-Factor-related installs. Never throws; inaccessible paths are skipped.
    /// </summary>
    public static IReadOnlyList<DiscoveredWorld> DiscoverAll()
    {
        var results = new List<DiscoveredWorld>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var clientRoot = Path.Combine(localAppData, "AbioticFactor", "Saved", "SaveGames");
        results.AddRange(DiscoverClientWorlds(clientRoot));

        // Microsoft Store / Game Pass: the packaged app redirects %LOCALAPPDATA% into
        // %LOCALAPPDATA%\Packages\<PackageFamilyName>\..., so the same client save tree lives there.
        results.AddRange(DiscoverPackagedClientWorlds(Path.Combine(localAppData, "Packages")));

        foreach (var library in AfInstallLocator.FindSteamLibraryRoots())
        {
            var common = Path.Combine(library, "steamapps", "common");
            results.AddRange(DiscoverServerWorlds(common));
        }

        return results
            .OrderBy(w => w.Source)
            .ThenBy(w => w.WorldName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Worlds under a client-style SaveGames root
    /// (<c>&lt;root&gt;\&lt;steamid&gt;\Worlds\&lt;World&gt;</c>). Exposed for tests.
    /// </summary>
    public static IReadOnlyList<DiscoveredWorld> DiscoverClientWorlds(string saveGamesRoot)
    {
        var results = new List<DiscoveredWorld>();
        if (!Directory.Exists(saveGamesRoot)) return results;

        try
        {
            foreach (var account in Directory.EnumerateDirectories(saveGamesRoot))
            {
                var accountId = Path.GetFileName(account);
                var worlds = Path.Combine(account, "Worlds");
                if (!Directory.Exists(worlds)) continue;

                foreach (var world in Directory.EnumerateDirectories(worlds))
                {
                    AddIfWorld(results, world, DiscoveredWorldSource.Client, accountId);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Discovery", $"Client world scan under {saveGamesRoot} failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Client-style worlds for a Microsoft Store / Game Pass install, whose packaged-app storage
    /// redirects <c>%LOCALAPPDATA%</c> into <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\...</c>.
    /// The package family name isn't hard-coded (it can change across store releases), so this
    /// probes the standard UE-on-Store redirect subpaths under every package folder and, for any
    /// package whose name mentions Abiotic, searches a little deeper for a moved <c>SaveGames</c>
    /// tree. Each root found is handed to <see cref="DiscoverClientWorlds"/>. Exposed for tests.
    /// </summary>
    public static IReadOnlyList<DiscoveredWorld> DiscoverPackagedClientWorlds(string packagesRoot)
    {
        var results = new List<DiscoveredWorld>();
        foreach (var saveGamesRoot in FindPackagedSaveGamesRoots(packagesRoot))
        {
            results.AddRange(DiscoverClientWorlds(saveGamesRoot));
        }
        return results;
    }

    // The UE-on-Microsoft-Store layouts seen in the wild: the engine writes its normal
    // AbioticFactor\Saved\SaveGames tree, but under the package's redirected local storage.
    private static readonly string[] PackagedSaveGamesSubPaths =
    {
        Path.Combine("LocalCache", "Local", "AbioticFactor", "Saved", "SaveGames"),
        Path.Combine("LocalCache", "Roaming", "AbioticFactor", "Saved", "SaveGames"),
        Path.Combine("LocalState", "AbioticFactor", "Saved", "SaveGames"),
        Path.Combine("AC", "AbioticFactor", "Saved", "SaveGames"),
    };

    private static IEnumerable<string> FindPackagedSaveGamesRoots(string packagesRoot)
    {
        if (!Directory.Exists(packagesRoot)) yield break;

        IEnumerable<string> packageDirs;
        try
        {
            packageDirs = Directory.EnumerateDirectories(packagesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packageDirs)
        {
            // Cheap, reliable: probe the known redirect subpaths.
            foreach (var sub in PackagedSaveGamesSubPaths)
            {
                var candidate = Path.Combine(package, sub);
                if (SafeDirectoryExists(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }

            // Fallback for layout drift, but only inside the actual game package (a deep search of
            // every installed package would be far too costly).
            if (Path.GetFileName(package).Contains("Abiotic", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var saveGames in FindDirectoriesNamed(package, "SaveGames", maxDepth: 8))
                {
                    if (seen.Add(saveGames))
                    {
                        yield return saveGames;
                    }
                }
            }
        }
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Worlds inside Abiotic-Factor-related installs under a Steam
    /// <c>steamapps\common</c> directory. Matches install folders containing "Abiotic"
    /// (the dedicated server tool and the game itself), then looks for
    /// <c>...\SaveGames\**\Worlds\&lt;World&gt;</c> trees a few levels deep.
    /// Exposed for tests with an arbitrary root.
    /// </summary>
    public static IReadOnlyList<DiscoveredWorld> DiscoverServerWorlds(string steamCommonDir)
    {
        var results = new List<DiscoveredWorld>();
        if (!Directory.Exists(steamCommonDir)) return results;

        try
        {
            foreach (var install in Directory.EnumerateDirectories(steamCommonDir))
            {
                var name = Path.GetFileName(install);
                if (!name.Contains("Abiotic", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var worldsDir in FindWorldsDirectories(install, maxDepth: 6))
                {
                    foreach (var world in Directory.EnumerateDirectories(worldsDir))
                    {
                        AddIfWorld(results, world, DiscoveredWorldSource.DedicatedServer, accountId: null);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Discovery", $"Server world scan under {steamCommonDir} failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Scans a single arbitrary root (a server download, a backup copy) for world
    /// folders, using the same rules as the Steam scan. Used by tests and by manual
    /// folder drops that point above a world folder.
    /// </summary>
    public static IReadOnlyList<DiscoveredWorld> DiscoverUnderRoot(string root, DiscoveredWorldSource source)
    {
        var results = new List<DiscoveredWorld>();
        if (!Directory.Exists(root)) return results;
        try
        {
            foreach (var worldsDir in FindWorldsDirectories(root, maxDepth: 6))
            {
                foreach (var world in Directory.EnumerateDirectories(worldsDir))
                {
                    AddIfWorld(results, world, source, accountId: null);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Discovery", $"World scan under {root} failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Directories named <c>Worlds</c>, breadth-first, depth-limited. The actual
    /// "is this a world" check happens per child via the .sav count, so an unrelated
    /// folder that happens to be called Worlds contributes nothing. <c>Backups</c>
    /// subtrees are skipped: they hold rotated copies of the same worlds.
    /// </summary>
    private static IEnumerable<string> FindWorldsDirectories(string root, int maxDepth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > maxDepth) continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Equals("Backups", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Equals("Worlds", StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                    continue;
                }
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    /// <summary>
    /// Directories with the given name, breadth-first and depth-limited, not descending past a
    /// match. Used to locate a <c>SaveGames</c> tree inside a Game Pass package whose internal
    /// layout we don't want to hard-code.
    /// </summary>
    private static IEnumerable<string> FindDirectoriesNamed(string root, string targetName, int maxDepth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > maxDepth) continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (Path.GetFileName(child).Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return child;
                    continue; // a SaveGames root's children are accounts, not more SaveGames dirs
                }
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static void AddIfWorld(
        List<DiscoveredWorld> results, string worldFolder, DiscoveredWorldSource source, string? accountId)
    {
        var savCount = 0;
        var lastPlayed = DateTime.MinValue;
        try
        {
            foreach (var sav in Directory.EnumerateFiles(worldFolder, "*.sav", SearchOption.AllDirectories))
            {
                // Ignore rotated backup copies so the count and "last played" reflect the
                // live saves, not a recently-written backup.
                if (sav.Contains($"{Path.DirectorySeparatorChar}Backups{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                savCount++;
                var written = File.GetLastWriteTime(sav);
                if (written > lastPlayed) lastPlayed = written;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (savCount == 0) return;

        results.Add(new DiscoveredWorld(
            Path.GetFullPath(worldFolder),
            Path.GetFileName(worldFolder),
            source,
            accountId)
        {
            SaveFileCount = savCount,
            LastPlayed = lastPlayed,
        });
    }
}
