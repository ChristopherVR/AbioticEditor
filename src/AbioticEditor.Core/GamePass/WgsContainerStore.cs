using System.Text;

namespace AbioticEditor.Core.GamePass;

/// <summary>One logical container in an Xbox "wgs" (Connected Storage) folder.</summary>
public sealed class WgsContainer
{
    public required string Name { get; init; }
    public required string Name2 { get; init; }
    public required string SyncId { get; set; }
    public byte ContainerNumber { get; set; }
    public uint Generation { get; set; }
    public required Guid FolderGuid { get; init; }
    public long FileTime { get; set; }
    public long Reserved { get; set; }
    public long BlobSize { get; set; }

    public string FolderName => FolderGuid.ToString("N").ToUpperInvariant();
}

/// <summary>
/// Reads and writes an Xbox "wgs" (Windows Game Saves / Connected Storage) folder - the on-disk
/// shape a Game Pass / Microsoft Store title uses instead of loose <c>.sav</c> files. The folder
/// holds a <c>containers.index</c> mapping logical container names to GUID sub-folders; each
/// sub-folder has a <c>container.N</c> manifest pointing at a GUID-named blob file (the actual
/// payload). See the project memory "Game Pass save format" for the byte layout.
///
/// <para>Writing a blob follows the game's own generation scheme: a fresh GUID blob is written, a
/// new <c>container.&lt;N+1&gt;</c> points at it, and the index entry is updated (number bumped,
/// new sync token, size, timestamp), leaving the previous generation in place for rollback. The
/// whole folder should be backed up first (callers use <see cref="GamePassSaveSet"/> which does).</para>
/// </summary>
public sealed class WgsContainerStore
{
    private const string IndexFileName = "containers.index";
    private const string BlobEntryName = "Data";
    private const int BlobNameFieldBytes = 128; // fixed UTF-16 field in container.N

    /// <summary>Abiotic Factor's Game Pass package family name + app id (public, identifies the title
    /// in a containers.index). Used when creating a container from scratch.</summary>
    public const string AbioticPackageFamilyName = "PlayStack.AbioticFactor_3wcqaesafpzfy!AppAbioticFactorShipping";

    private readonly string _root;

    // Verbatim header bytes (everything before the first entry) - preserved on rewrite.
    private byte[] _header = Array.Empty<byte>();
    private uint _version;

    public IReadOnlyList<WgsContainer> Containers { get; private set; } = Array.Empty<WgsContainer>();

    private readonly List<string> _recoveredContainers = new();

    /// <summary>
    /// Logical containers whose manifest pointed at a blob that was missing from disk, so a sibling
    /// blob had to be used instead (see <see cref="ReadBlob"/>). A non-empty list is a reliable sign
    /// the save is mid-Xbox-sync: the index and the on-disk blobs disagree because cloud sync has not
    /// finished. Writing into a store in this state is what lets Xbox later discard the edited
    /// containers, so the host should warn before allowing edits.
    /// </summary>
    public IReadOnlyList<string> RecoveredContainers => _recoveredContainers;

    /// <summary>True when any container was read through the missing-blob fallback (save is mid-sync).</summary>
    public bool NeededBlobFallback => _recoveredContainers.Count > 0;

    /// <summary>The package family name recorded in the index (identifies the owning title).</summary>
    public string PackageFamilyName { get; private set; } = string.Empty;

    /// <summary>True when <paramref name="folder"/> is a wgs container store for Abiotic Factor
    /// (the index names the Abiotic package). Cheap: reads only the index, no decompression.</summary>
    public static bool IsAbioticContainerFolder(string folder)
    {
        if (!IsContainerFolder(folder)) return false;
        try
        {
            return Open(folder).PackageFamilyName.Contains("Abiotic", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private WgsContainerStore(string root) => _root = root;

    /// <summary>True when <paramref name="folder"/> directly contains a <c>containers.index</c>.</summary>
    public static bool IsContainerFolder(string folder)
        => File.Exists(Path.Combine(folder, IndexFileName));

    /// <summary>
    /// Maps a folder the user picked to the actual wgs container folder (the one holding
    /// <c>containers.index</c>), tolerating the levels a Game Pass save tree invites a mis-click on:
    /// the container folder itself, its <c>wgs</c> / account parent (the picked folder has a child
    /// that is a container folder), or a GUID blob sub-folder (the picked folder's parent is the
    /// container folder). Returns null when nothing nearby is a container folder. Best-effort: an
    /// unreadable folder yields null rather than throwing.
    /// </summary>
    public static string? ResolveContainerFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            if (IsContainerFolder(folder)) return folder;

            // Picked one level up (e.g. the "wgs" folder): a child is the account/container folder.
            if (Directory.Exists(folder))
            {
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    if (IsContainerFolder(child)) return child;
                }
            }

            // Picked a GUID blob sub-folder: its parent is the container folder.
            var parent = Directory.GetParent(folder)?.FullName;
            if (parent is not null && IsContainerFolder(parent)) return parent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable folder: treat as "not a container folder".
        }
        return null;
    }

    public static WgsContainerStore Open(string folder)
    {
        var store = new WgsContainerStore(folder);
        store.Load();
        return store;
    }

    private void Load()
    {
        var indexPath = Path.Combine(_root, IndexFileName);
        var d = File.ReadAllBytes(indexPath);
        var pos = 0;

        _version = ReadU32(d, ref pos);
        var count = ReadU32(d, ref pos);
        _ = ReadU32(d, ref pos);            // reserved (0)
        PackageFamilyName = ReadWString(d, ref pos);
        pos += 8;                           // index FILETIME
        ReadU32(d, ref pos);                // constant (3)
        ReadWString(d, ref pos);            // root GUID string
        pos += 8;                           // 8 reserved bytes

        _header = d[..pos];

        var list = new List<WgsContainer>((int)count);
        for (var i = 0; i < count; i++)
        {
            var name = ReadWString(d, ref pos);
            var name2 = ReadWString(d, ref pos);
            var sync = ReadWString(d, ref pos);
            var num = d[pos]; pos += 1;
            var gen = ReadU32(d, ref pos);
            var folder = new Guid(d.AsSpan(pos, 16).ToArray()); pos += 16;
            var ft = ReadI64(d, ref pos);
            var reserved = ReadI64(d, ref pos);
            var size = ReadI64(d, ref pos);
            list.Add(new WgsContainer
            {
                Name = name,
                Name2 = name2,
                SyncId = sync,
                ContainerNumber = num,
                Generation = gen,
                FolderGuid = folder,
                FileTime = ft,
                Reserved = reserved,
                BlobSize = size,
            });
        }
        Containers = list;
    }

    public WgsContainer? Find(string name)
        => Containers.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when <paramref name="folder"/> holds GUID container sub-folders (each with a
    /// <c>container.N</c> manifest) that the current <c>containers.index</c> no longer references -
    /// the fingerprint of a container Xbox cloud sync dropped from the index while leaving its data
    /// on disk. Used to tell the user a "missing" Game Pass world is actually recoverable. Best
    /// effort: an unreadable folder returns false rather than throwing.
    /// </summary>
    public static bool HasOrphanedWorldFolders(string folder)
    {
        try
        {
            if (!IsContainerFolder(folder)) return false;
            var store = Open(folder);
            var referenced = new HashSet<string>(
                store.Containers.Select(c => c.FolderName), StringComparer.OrdinalIgnoreCase);

            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(sub);
                // GUID "N" sub-folders are 32 hex chars; skip anything else (and referenced ones).
                if (name.Length != 32 || !IsHex(name)) continue;
                if (referenced.Contains(name)) continue;
                // An orphan that still carries a container.N manifest is real, recoverable save data.
                if (Directory.EnumerateFiles(sub, "container.*").Any()) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an unreadable tree as "nothing recoverable detected".
        }
        return false;
    }

    /// <summary>Reads the blob bytes for a logical container (via its <c>container.N</c> manifest).</summary>
    public byte[] ReadBlob(WgsContainer container)
    {
        var folder = Path.Combine(_root, container.FolderName);
        var blobGuid = ReadManifestBlobGuid(folder, container.ContainerNumber);
        var blobPath = Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant());
        if (File.Exists(blobPath))
            return File.ReadAllBytes(blobPath);

        // The expected blob is missing - this happens when Xbox Connected Storage updated the
        // container manifest (or synced one from the cloud) but the corresponding blob file was
        // never written or was renamed. Look for the only other GUID-named blob in the folder;
        // if exactly one exists and its size matches the index record, use it with a warning.
        var fallback = FindFallbackBlob(folder, blobGuid, container.BlobSize);
        if (fallback is not null)
        {
            if (!_recoveredContainers.Contains(container.Name, StringComparer.OrdinalIgnoreCase))
                _recoveredContainers.Add(container.Name);
            Diagnostics.EditorLog.Warn("GamePass",
                $"Save blob '{blobGuid:N}' for '{container.Name}' not found on disk - " +
                $"using existing blob '{Path.GetFileName(fallback)}' as a fallback. " +
                "This means Xbox cloud sync has not finished for this save; reading works but writing " +
                "now risks Xbox discarding the change. The save was read successfully.");
            return File.ReadAllBytes(fallback);
        }

        throw new InvalidDataException(
            $"Save data blob for '{container.Name}' is missing (expected {blobGuid:N}). " +
            "Xbox cloud sync may not have finished downloading this save - " +
            "close the game completely, wait for sync to complete, and try again.");
    }

    /// <summary>
    /// Permanently fixes every container that was read through the missing-blob fallback: its
    /// <c>container.N</c> manifest is rewritten to point at the blob actually present on disk, and the
    /// index entry's size is corrected to match. This turns a save that is permanently inconsistent
    /// (the manifest names a blob that never existed locally - a leftover from an interrupted Xbox
    /// sync that will never download) into a self-consistent one, so reopening no longer needs the
    /// fallback and the "mid-sync" warning stops. It only repairs the pointer, never the save data.
    /// Returns the container names repaired. Call with the game and Xbox app closed.
    /// </summary>
    public IReadOnlyList<string> RepairRecoveredManifests()
    {
        var repaired = new List<string>();
        var indexNeedsRewrite = false;

        // Scan every container, not just the ones already read this session - the whole point is to
        // leave the folder fully consistent so Xbox has nothing left to reconcile away.
        foreach (var container in Containers)
        {
            var folder = Path.Combine(_root, container.FolderName);
            Guid expected;
            try { expected = ReadManifestBlobGuid(folder, container.ContainerNumber); }
            catch { continue; }

            // Already consistent - the manifest points at a blob that is on disk.
            if (File.Exists(Path.Combine(folder, expected.ToString("N").ToUpperInvariant())))
            {
                _recoveredContainers.Remove(container.Name);
                continue;
            }

            var fallback = FindFallbackBlob(folder, expected, container.BlobSize);
            if (fallback is null) continue;
            var fallbackName = Path.GetFileName(fallback);
            if (!Guid.TryParseExact(fallbackName, "N", out var fallbackGuid)) continue;

            WriteManifest(folder, container.ContainerNumber, fallbackGuid);
            // Keep the index entry's recorded size in step with the blob we just pointed at.
            var actualSize = new FileInfo(fallback).Length;
            if (container.BlobSize != actualSize) { container.BlobSize = actualSize; indexNeedsRewrite = true; }

            repaired.Add(container.Name);
            _recoveredContainers.Remove(container.Name);
            Diagnostics.EditorLog.Info("GamePass",
                $"Repaired container '{container.Name}': container.{container.ContainerNumber} now points at on-disk blob '{fallbackName}'.");
        }

        if (indexNeedsRewrite) WriteIndex();
        return repaired;
    }

    /// <summary>
    /// Looks for a GUID-named blob file in <paramref name="folder"/> that could serve as a
    /// fallback when the manifest-referenced blob is absent. Returns the path to use, or null.
    /// Only returns a result when there is no ambiguity: exactly one candidate exists, or exactly
    /// one candidate matches the expected size.
    /// </summary>
    private static string? FindFallbackBlob(string folder, Guid expectedGuid, long expectedSize)
    {
        var expected = expectedGuid.ToString("N").ToUpperInvariant();
        var all = new List<string>();
        var sizeMatch = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("container.", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) continue;
            // Only treat 32-hex-char names as blob files (GUID "N" format).
            if (name.Length != 32 || !IsHex(name)) continue;
            all.Add(file);
            if (expectedSize > 0 && new FileInfo(file).Length == expectedSize)
                sizeMatch.Add(file);
        }
        if (sizeMatch.Count == 1) return sizeMatch[0];
        if (all.Count == 1) return all[0];
        return null;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }

    /// <summary>
    /// Writes new blob bytes for a logical container: a fresh GUID blob file, a new
    /// <c>container.&lt;N+1&gt;</c> manifest, and an updated index entry. Rewrites
    /// <c>containers.index</c>. The previous generation's files are left for rollback.
    /// </summary>
    public void WriteBlob(WgsContainer container, byte[] blob)
    {
        var folder = Path.Combine(_root, container.FolderName);
        Directory.CreateDirectory(folder);

        var newBlobGuid = Guid.NewGuid();
        var blobPath = Path.Combine(folder, newBlobGuid.ToString("N").ToUpperInvariant());
        File.WriteAllBytes(blobPath, blob);

        var newNumber = unchecked((byte)(container.ContainerNumber + 1));
        WriteManifest(folder, newNumber, newBlobGuid);

        container.ContainerNumber = newNumber;
        container.Generation = unchecked(container.Generation + 1);
        container.BlobSize = blob.Length;
        container.FileTime = DateTime.UtcNow.ToFileTimeUtc();
        container.SyncId = $"\"0x{DateTime.UtcNow.ToFileTimeUtc():X}\"";

        WriteIndex();
        Diagnostics.EditorLog.Info("GamePass",
            $"wgs: wrote container '{container.Name}' gen {newNumber} ({blob.Length} bytes).");
    }

    /// <summary>
    /// Creates a brand-new single-container wgs folder at <paramref name="destFolder"/> holding one
    /// logical container (<paramref name="containerName"/>) whose blob is <paramref name="blob"/>.
    /// Writes <c>containers.index</c>, the GUID container folder, its <c>container.1</c> manifest and
    /// the blob. Used to convert a Steam world into a Game Pass save.
    /// </summary>
    public static void WriteNewContainer(string destFolder, string containerName, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        Directory.CreateDirectory(destFolder);

        var folderGuid = Guid.NewGuid();
        var folder = Path.Combine(destFolder, folderGuid.ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(folder);

        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);
        WriteManifest(folder, 1, blobGuid);

        var now = DateTime.UtcNow.ToFileTimeUtc();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        w.Write(14u);                              // version
        w.Write(1u);                               // container count
        w.Write(0u);                               // reserved
        WriteWString(w, AbioticPackageFamilyName);
        w.Write(now);                              // index FILETIME
        w.Write(3u);                               // constant
        WriteWString(w, Guid.NewGuid().ToString());// root GUID
        w.Write(new byte[] { 0, 0, 0, 0x10, 0, 0, 0, 0 }); // 8 reserved bytes (as the game writes)
        WriteWString(w, containerName);
        WriteWString(w, containerName);
        WriteWString(w, $"\"0x{now:X}\"");         // sync token
        w.Write((byte)1);                          // container number -> container.1
        w.Write(1u);                               // generation
        w.Write(folderGuid.ToByteArray());
        w.Write(now);                              // entry FILETIME
        w.Write(0L);                               // reserved
        w.Write((long)blob.Length);
        w.Flush();
        File.WriteAllBytes(Path.Combine(destFolder, IndexFileName), ms.ToArray());
        Diagnostics.EditorLog.Info("GamePass", $"Created wgs container '{containerName}' at {destFolder} ({blob.Length} bytes).");
    }

    private static Guid ReadManifestBlobGuid(string folder, byte number)
    {
        var path = Path.Combine(folder, $"container.{number}");
        var d = File.ReadAllBytes(path);
        var pos = 0;
        ReadU32(d, ref pos);                 // constant (4)
        var blobCount = ReadU32(d, ref pos); // blob entries (1)
        if (blobCount < 1) throw new InvalidDataException($"{path} declares no blobs.");
        pos += BlobNameFieldBytes;           // fixed "Data" name field
        return new Guid(d.AsSpan(pos, 16).ToArray());
    }

    private static void WriteManifest(string folder, byte number, Guid blobGuid)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(4u);
        w.Write(1u);
        var nameField = new byte[BlobNameFieldBytes];
        Encoding.Unicode.GetBytes(BlobEntryName).CopyTo(nameField, 0);
        w.Write(nameField);
        w.Write(blobGuid.ToByteArray());
        w.Write(blobGuid.ToByteArray()); // duplicated (current + baseline)
        File.WriteAllBytes(Path.Combine(folder, $"container.{number}"), ms.ToArray());
    }

    private void WriteIndex()
    {
        using var ms = new MemoryStream();
        ms.Write(_header, 0, _header.Length);
        using var w = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        foreach (var c in Containers)
        {
            WriteWString(w, c.Name);
            WriteWString(w, c.Name2);
            WriteWString(w, c.SyncId);
            w.Write(c.ContainerNumber);
            w.Write(c.Generation);
            w.Write(c.FolderGuid.ToByteArray());
            w.Write(c.FileTime);
            w.Write(c.Reserved);
            w.Write(c.BlobSize);
        }
        w.Flush();
        File.WriteAllBytes(Path.Combine(_root, IndexFileName), ms.ToArray());
    }

    private static uint ReadU32(byte[] d, ref int p) { var v = BitConverter.ToUInt32(d, p); p += 4; return v; }
    private static long ReadI64(byte[] d, ref int p) { var v = BitConverter.ToInt64(d, p); p += 8; return v; }

    private static string ReadWString(byte[] d, ref int p)
    {
        var n = (int)ReadU32(d, ref p);
        var s = Encoding.Unicode.GetString(d, p, n * 2);
        p += n * 2;
        return s;
    }

    private static void WriteWString(BinaryWriter w, string s)
    {
        w.Write((uint)s.Length);
        w.Write(Encoding.Unicode.GetBytes(s));
    }
}
