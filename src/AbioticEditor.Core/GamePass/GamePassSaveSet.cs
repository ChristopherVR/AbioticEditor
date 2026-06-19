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
/// Finds Abiotic Factor's Game Pass / Microsoft Store saves: the packaged app stores them under
/// <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\SystemAppData\wgs\&lt;XUID&gt;_&lt;...&gt;\</c>.
/// The publisher-hash suffix of the package name varies, so any package whose name mentions Abiotic
/// is searched. Never throws; inaccessible paths are skipped.
/// </summary>
public static class GamePassDiscovery
{
    public static IReadOnlyList<DiscoveredGamePassSave> DiscoverAll()
    {
        var results = new List<DiscoveredGamePassSave>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packages = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packages)) return results;

        IEnumerable<string> abioticPackages;
        try
        {
            abioticPackages = Directory.EnumerateDirectories(packages)
                .Where(p => Path.GetFileName(p).Contains("Abiotic", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return results;
        }

        foreach (var package in abioticPackages)
        {
            var wgs = Path.Combine(package, "SystemAppData", "wgs");
            if (!SafeExists(wgs)) continue;
            foreach (var accountDir in SafeDirs(wgs))
            {
                if (!WgsContainerStore.IsContainerFolder(accountDir)) continue;
                results.Add(new DiscoveredGamePassSave(
                    Path.GetFullPath(accountDir),
                    ParseAccountId(Path.GetFileName(accountDir)))
                {
                    LastModified = LastWrite(accountDir),
                });
            }
        }
        return results;
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
