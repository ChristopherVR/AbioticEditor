namespace AbioticEditor.Core.GamePass;

/// <summary>The kind of editable save a Game Pass bundle member maps to.</summary>
public enum GamePassSaveKind
{
    Player,
    World,
    WorldMetadata,
    Other,
}

/// <summary>
/// One editable save inside a Game Pass world bundle, presented as if it were a loose
/// <c>.sav</c> file so the existing readers/writers can consume it.
/// </summary>
public sealed class GamePassSaveEntry
{
    public required string ContainerName { get; init; }   // wgs logical container, e.g. "ForScience-WC"
    public required string WorldName { get; init; }       // e.g. "ForScience"
    public required string MemberPath { get; init; }      // in-bundle path
    public required string SaveClass { get; init; }
    public required GamePassSaveKind Kind { get; init; }

    /// <summary>The <c>Player_&lt;id&gt;.sav</c> / <c>WorldSave_*.sav</c> file name. In-bundle paths
    /// drop the extension, so it is re-added to match how the rest of the editor names saves.</summary>
    public string FileName
    {
        get
        {
            var name = Path.GetFileName(MemberPath.Replace('\\', '/'));
            return name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? name : name + ".sav";
        }
    }

    /// <summary>True for members the editor can open (player + world GVAS saves).</summary>
    public bool IsEditable => Kind != GamePassSaveKind.Other;
}

/// <summary>
/// High-level view of one Xbox "wgs" folder for Abiotic Factor: it surfaces the world/player saves
/// packed inside each world container (<c>&lt;World&gt;-WC</c>) as virtual <c>.sav</c> files, hands
/// the existing readers reconstructed GVAS bytes, and writes edits back through the bundle + wgs
/// layers (Oodle recompress, new blob generation) with a one-time backup of the whole folder.
/// </summary>
public sealed class GamePassSaveSet
{
    private readonly WgsContainerStore _store;
    private readonly Dictionary<string, AbfSaveBundle> _bundles = new(StringComparer.OrdinalIgnoreCase);
    private bool _backedUp;

    public string FolderPath { get; }

    private GamePassSaveSet(string folder, WgsContainerStore store)
    {
        FolderPath = folder;
        _store = store;
    }

    /// <summary>True when <paramref name="folder"/> is a wgs container folder.</summary>
    public static bool IsGamePassFolder(string folder) => WgsContainerStore.IsContainerFolder(folder);

    public static GamePassSaveSet Open(string folder)
    {
        var store = WgsContainerStore.Open(folder);
        return new GamePassSaveSet(folder, store);
    }

    /// <summary>Every editable player/world save across all world containers in this folder.</summary>
    public IReadOnlyList<GamePassSaveEntry> Entries()
    {
        var entries = new List<GamePassSaveEntry>();
        foreach (var container in _store.Containers)
        {
            // World bundles are the "-WC" containers; "-WC-B" are backups, others are profile/settings.
            if (!container.Name.EndsWith("-WC", StringComparison.OrdinalIgnoreCase)) continue;

            AbfSaveBundle bundle;
            try
            {
                bundle = LoadBundle(container.Name);
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("GamePass", $"Could not read bundle '{container.Name}': {ex.Message}");
                continue;
            }

            var world = container.Name[..^"-WC".Length];
            foreach (var m in bundle.Members)
            {
                entries.Add(new GamePassSaveEntry
                {
                    ContainerName = container.Name,
                    WorldName = world,
                    MemberPath = m.Path,
                    SaveClass = m.SaveClass,
                    Kind = KindOf(m.SaveClass),
                });
            }
        }
        return entries;
    }

    /// <summary>Reconstructs a full GVAS save the editor can parse for the given entry.</summary>
    public byte[] ReadSave(GamePassSaveEntry entry)
    {
        var member = Member(entry);
        return GamePassMemberCodec.ToGvas(entry.SaveClass, member.Body);
    }

    /// <summary>
    /// Writes an edited GVAS save back: strips it to a member body, repacks the world bundle
    /// (Oodle), and writes a new blob generation. Backs up the whole wgs folder on first write.
    /// </summary>
    public void WriteSave(GamePassSaveEntry entry, byte[] editedGvas)
    {
        ArgumentNullException.ThrowIfNull(editedGvas);
        BackupOnce();
        Member(entry).Body = GamePassMemberCodec.ToMemberBody(entry.SaveClass, editedGvas);
        Repack(entry.ContainerName);
    }

    /// <summary>
    /// Extracts every editable save in <paramref name="containerName"/> to <paramref name="destDir"/>
    /// as loose <c>.sav</c> files in the normal world layout (<c>WorldSave_*.sav</c> at the top,
    /// <c>PlayerData/Player_*.sav</c> underneath) so the standard folder editor can open them.
    /// Returns the world name.
    /// </summary>
    public string ExtractWorld(string containerName, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string world = containerName;
        foreach (var entry in Entries().Where(e => e.ContainerName.Equals(containerName, StringComparison.OrdinalIgnoreCase) && e.IsEditable))
        {
            world = entry.WorldName;
            var path = ResolveMemberPath(entry, destDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, ReadSave(entry));
        }
        // Nothing was extracted for this container. Call LoadBundle directly so the real cause
        // (missing blob, bad Oodle stream, etc.) propagates to the caller instead of silently
        // producing an empty folder that shows as a blank sidebar.
        if (string.Equals(world, containerName, StringComparison.OrdinalIgnoreCase))
        {
            LoadBundle(containerName); // rethrows the bundle-load error if one occurred
            throw new InvalidOperationException($"Container '{containerName}' has no editable saves.");
        }
        return world;
    }

    /// <summary>
    /// Re-packs every edited <c>.sav</c> under <paramref name="srcDir"/> (as laid out by
    /// <see cref="ExtractWorld"/>) back into <paramref name="containerName"/> in one pass: each
    /// member body is refreshed from disk, the bundle is Oodle-recompressed once, and a single new
    /// blob generation is written. Backs up the wgs folder on first write. Returns the count written.
    /// </summary>
    public int ApplyWorld(string containerName, string srcDir)
    {
        BackupOnce();
        var changed = 0;
        foreach (var entry in Entries().Where(e => e.ContainerName.Equals(containerName, StringComparison.OrdinalIgnoreCase) && e.IsEditable))
        {
            var path = ResolveMemberPath(entry, srcDir);
            if (!File.Exists(path)) continue;
            Member(entry).Body = GamePassMemberCodec.ToMemberBody(entry.SaveClass, File.ReadAllBytes(path));
            changed++;
        }
        if (changed > 0) Repack(containerName);
        return changed;
    }

    /// <summary>
    /// Builds the working-copy path for a member and validates it stays inside
    /// <paramref name="baseDir"/>. The member's name comes from a bundle TOC path inside a save the
    /// user opened, so it is untrusted; this guards against a crafted container writing outside the
    /// working folder (zip-slip), on top of the leaf-only <see cref="GamePassSaveEntry.FileName"/>.
    /// </summary>
    private static string ResolveMemberPath(GamePassSaveEntry entry, string baseDir)
    {
        var relative = entry.Kind == GamePassSaveKind.Player
            ? Path.Combine("PlayerData", entry.FileName)
            : entry.FileName;
        var root = Path.GetFullPath(baseDir);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Game Pass member '{entry.FileName}' resolves outside the working directory - extraction aborted.");
        }
        return full;
    }

    private void Repack(string containerName)
    {
        var blob = _bundles[containerName].Serialize();
        var container = _store.Find(containerName)
            ?? throw new InvalidOperationException($"Container '{containerName}' vanished.");
        _store.WriteBlob(container, blob);
    }

    private AbfSaveBundle LoadBundle(string containerName)
    {
        if (_bundles.TryGetValue(containerName, out var cached)) return cached;
        var container = _store.Find(containerName)
            ?? throw new InvalidOperationException($"No container '{containerName}'.");
        var blob = _store.ReadBlob(container);
        if (!AbfSaveBundle.LooksLikeBundle(blob))
        {
            throw new InvalidDataException($"Container '{containerName}' is not an ABF_SAVE_VERSION bundle.");
        }
        var bundle = AbfSaveBundle.Parse(blob);
        _bundles[containerName] = bundle;
        return bundle;
    }

    private AbfMember Member(GamePassSaveEntry entry)
    {
        var bundle = LoadBundle(entry.ContainerName);
        return bundle.Members.FirstOrDefault(m =>
                   string.Equals(m.Path, entry.MemberPath, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Member '{entry.MemberPath}' not found.");
    }

    private void BackupOnce()
    {
        if (_backedUp) return;
        var dest = FolderPath.TrimEnd('/', '\\') + ".bak";
        if (Directory.Exists(dest))
        {
            dest += "-" + DateTime.UtcNow.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        CopyDirectory(FolderPath, dest);
        _backedUp = true;
        Diagnostics.EditorLog.Info("GamePass", $"Backed up wgs folder to {dest}");
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }

    private static GamePassSaveKind KindOf(string saveClass) => saveClass switch
    {
        GamePassMemberCodec.CharacterSaveClass => GamePassSaveKind.Player,
        GamePassMemberCodec.WorldSaveClass => GamePassSaveKind.World,
        GamePassMemberCodec.WorldMetadataSaveClass => GamePassSaveKind.WorldMetadata,
        _ => GamePassSaveKind.Other,
    };
}

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
                if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.EditorLog.Info("GamePass",
                        $"Discovery: '{name}' is a backup folder - skipped.");
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
