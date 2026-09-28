using System.Security.Cryptography;
using System.Text;

namespace AbioticEditor.GamePass.Storage;

/// <summary>
/// Reads and writes an Xbox "wgs" (Connected Storage) folder: a <c>containers.index</c> mapping
/// logical container names to GUID sub-folders, each holding a <c>container.N</c> manifest that
/// names a GUID blob. Game-agnostic: the payload is an opaque byte array, and anything game
/// specific reaches the store through <see cref="WgsStoreOptions"/>.
///
/// <para>Writing follows the game's own scheme: fresh blob, then the manifest that names it, then
/// the index that names the manifest, then the superseded generation is removed. A crash at any
/// point leaves the previous generation fully described. This is NOT a transaction across the
/// store; use <see cref="DetectExternalChange"/> (or the <c>Try*</c> commit methods) to notice a
/// game or sync client that changed the store since it was inspected.</para>
///
/// <para>The folder is one half of a conversation with the Xbox cloud. State is set to what
/// actually happened, the ETag is echoed rather than invented, and unknown fields round-trip
/// verbatim. See docs/reference/game-pass-format.md.</para>
/// </summary>
public sealed class WgsStore
{
    public const string IndexFileName = "containers.index";
    private const string BlobEntryName = "Data";
    private const int BlobNameFieldBytes = 128; // fixed UTF-16 field in container.N

    /// <summary>The only index version observed in real stores.</summary>
    public const uint KnownIndexVersion = 14;

    private readonly string _root;
    private readonly WgsStoreOptions _options;
    private readonly IWgsFileSystem _fs;

    private byte[] _header = [];
    private List<WgsContainer> _containers = [];
    private readonly List<string> _recoveredContainers = [];
    private int _syncFlagsOffset;
    private int _indexFileTimeOffset;
    private string _indexFingerprint = string.Empty;

    private WgsStore(string root, WgsStoreOptions options)
    {
        _root = root;
        _options = options;
        _fs = options.FileSystem;
    }

    public string RootPath => _root;
    public uint IndexVersion { get; private set; }
    public IReadOnlyList<WgsContainer> Containers => _containers;

    /// <summary>Containers read through the missing-blob fallback: a reliable sign the store is mid-sync.</summary>
    public IReadOnlyList<string> RecoveredContainers => _recoveredContainers;

    public bool NeededBlobFallback => _recoveredContainers.Count > 0;

    /// <summary>The package family name recorded in the index (identifies the owning title).</summary>
    public string PackageFamilyName { get; private set; } = string.Empty;

    /// <summary>The index-level FILETIME: the recency token cloud sync compares.</summary>
    public long IndexFileTime { get; private set; }

    public WgsSyncState SyncState { get; private set; }

    public bool HasUnresolvedConflicts => SyncState.HasFlag(WgsSyncState.HasUnresolvedConflicts);

    /// <summary>Containers whose state is outside the format, or whose state contradicts their ETag.</summary>
    public IReadOnlyList<string> InvalidStateContainers
        => _containers.Where(c => c.HasInvalidState || c.StateContradictsEtag).Select(c => c.Name).ToList();

    /// <summary>Containers a write must not build on: undefined state, or a deletion tombstone.</summary>
    public IReadOnlyList<string> UnsafeStateContainers
        => _containers.Where(c => c.HasInvalidState || c.State == WgsEntryState.Deleted)
            .Select(c => c.Name).ToList();

    /// <summary>Containers whose state and ETag contradict each other (a write puts them right).</summary>
    public IReadOnlyList<string> ContradictoryStateContainers
        => _containers.Where(c => !c.HasInvalidState && c.StateContradictsEtag).Select(c => c.Name).ToList();

    public WgsContainer? Find(string name)
        => _containers.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------------------------
    // Opening
    // ------------------------------------------------------------------------------------

    /// <summary>True when <paramref name="folder"/> directly contains a <c>containers.index</c>.</summary>
    public static bool IsContainerFolder(string folder, IWgsFileSystem? fileSystem = null)
        => (fileSystem ?? PhysicalWgsFileSystem.Instance).FileExists(Path.Combine(folder, IndexFileName));

    /// <summary>
    /// Maps a folder a user picked to the folder holding <c>containers.index</c>: the folder itself,
    /// a parent whose child is one, or a GUID sub-folder whose parent is one. Null when nothing
    /// nearby is a container folder. Never throws on an unreadable folder.
    /// </summary>
    public static string? ResolveContainerFolder(string folder, IWgsFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var fs = fileSystem ?? PhysicalWgsFileSystem.Instance;
        try
        {
            if (IsContainerFolder(folder, fs)) return folder;
            if (fs.DirectoryExists(folder))
            {
                foreach (var child in fs.EnumerateDirectories(folder))
                {
                    if (IsContainerFolder(child, fs)) return child;
                }
            }
            var parent = Directory.GetParent(folder)?.FullName;
            if (parent is not null && IsContainerFolder(parent, fs)) return parent;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable folder: treat as "not a container folder".
        }
        return null;
    }

    /// <summary>Opens a store, throwing on any failure (see <see cref="TryOpen"/> for typed results).</summary>
    public static WgsStore Open(string folder, WgsStoreOptions? options = null)
    {
        var store = new WgsStore(folder, options ?? WgsStoreOptions.Default);
        store.Load();
        return store;
    }

    /// <summary>Opens a store and reports lock conflicts and unsupported layouts as typed results.</summary>
    public static WgsOpenResult TryOpen(string folder, WgsStoreOptions? options = null)
    {
        var opts = options ?? WgsStoreOptions.Default;
        if (!IsContainerFolder(folder, opts.FileSystem))
        {
            return new WgsOpenResult(WgsOpenStatus.NotAContainerFolder, null,
                $"'{folder}' does not contain {IndexFileName}.");
        }
        try
        {
            return new WgsOpenResult(WgsOpenStatus.Opened, Open(folder, opts), null);
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            return new WgsOpenResult(WgsOpenStatus.LockConflict, null, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IndexOutOfRangeException
            or OverflowException or FormatException)
        {
            return new WgsOpenResult(WgsOpenStatus.UnsupportedLayout, null,
                $"{IndexFileName} is not in a layout this package understands: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WgsOpenResult(WgsOpenStatus.Failed, null, ex.Message);
        }
    }

    private void Load()
    {
        var d = _fs.ReadAllBytes(Path.Combine(_root, IndexFileName));
        _indexFingerprint = Fingerprint(d);
        var pos = 0;

        IndexVersion = ReadU32(d, ref pos);
        var count = ReadU32(d, ref pos);
        _ = ReadU32(d, ref pos);            // reserved (0): really an empty length-prefixed display name
        PackageFamilyName = ReadWString(d, ref pos);
        // Record where the timestamp actually sits rather than recomputing it from the name's length.
        _indexFileTimeOffset = pos;
        IndexFileTime = ReadI64(d, ref pos);
        _syncFlagsOffset = pos;
        SyncState = (WgsSyncState)ReadU32(d, ref pos);
        ReadWString(d, ref pos);            // root GUID string
        pos += 8;                           // 8 reserved bytes

        _header = d[..pos];

        var list = new List<WgsContainer>((int)Math.Min(count, 4096));
        for (var i = 0; i < count; i++)
        {
            var name = ReadWString(d, ref pos);
            var name2 = ReadWString(d, ref pos);
            var etag = ReadWString(d, ref pos);
            var num = d[pos]; pos += 1;
            var state = ReadU32(d, ref pos);
            var folder = new Guid(d.AsSpan(pos, 16).ToArray()); pos += 16;
            var ft = ReadI64(d, ref pos);
            var reserved = ReadI64(d, ref pos);
            var size = ReadI64(d, ref pos);
            list.Add(new WgsContainer
            {
                Name = name,
                Name2 = name2,
                Etag = etag,
                ContainerNumber = num,
                RawState = state,
                State = state <= (uint)WgsEntryState.Created ? (WgsEntryState)state : WgsEntryState.Modified,
                FolderGuid = folder,
                FileTime = ft,
                Reserved = reserved,
                BlobSize = size,
            });
        }
        _containers = list;
    }

    // ------------------------------------------------------------------------------------
    // Change detection, diagnosis, planning
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Compares the index on disk with the one this instance last read or wrote. A change means the
    /// game or a sync client touched the store between inspection and now: re-open and re-evaluate
    /// before writing. Reads only; never throws (an unreadable index is reported as changed).
    /// </summary>
    public WgsChangeReport DetectExternalChange()
    {
        try
        {
            var path = Path.Combine(_root, IndexFileName);
            if (!_fs.FileExists(path))
            {
                return new WgsChangeReport(true, [$"{IndexFileName} no longer exists."]);
            }
            var now = Fingerprint(_fs.ReadAllBytes(path));
            return now == _indexFingerprint
                ? WgsChangeReport.Unchanged
                : new WgsChangeReport(true, [$"{IndexFileName} changed on disk since it was read."]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WgsChangeReport(true, [$"{IndexFileName} could not be re-read: {ex.Message}"]);
        }
    }

    /// <summary>Everything worth knowing about the store's health. Changes nothing.</summary>
    public WgsDiagnosis Diagnose()
    {
        var multi = new List<string>();
        foreach (var c in _containers)
        {
            try
            {
                if (ReadManifest(Path.Combine(_root, c.FolderName), c.ContainerNumber).BlobCount != 1)
                {
                    multi.Add(c.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentException)
            {
                // A manifest that cannot be read is not something Diagnose can classify.
            }
        }
        return new WgsDiagnosis(
            IndexVersion, IndexVersion == KnownIndexVersion, PackageFamilyName, SyncState, _containers.Count,
            InvalidStateContainers, UnsafeStateContainers, ContainersNeedingRepair(), multi,
            OrphanedContainers(), AssessWrite());
    }

    /// <summary>The write gate's verdict right now.</summary>
    public WgsWriteAssessment AssessWrite() => (_options.WriteGate ?? WgsWriteGates.Structural).Assess(this);

    /// <summary>Throws <see cref="WgsWriteRefusedException"/> when the write gate refuses.</summary>
    public void EnsureWritable()
    {
        var assessment = AssessWrite();
        if (!assessment.CanWrite) throw new WgsWriteRefusedException(assessment);
    }

    /// <summary>Previews what <see cref="WriteBlob"/> would do for one container. Touches nothing.</summary>
    public WgsWritePlan PlanWrite(WgsContainer container, long blobLength)
    {
        ArgumentNullException.ThrowIfNull(container);
        var folder = Path.Combine(_root, container.FolderName);
        var newNumber = unchecked((byte)(container.ContainerNumber + 1));
        var state = string.IsNullOrEmpty(container.Etag) ? WgsEntryState.Created : WgsEntryState.Modified;
        var remove = new List<string>();
        if (_fs.DirectoryExists(folder))
        {
            foreach (var file in _fs.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("container.", StringComparison.OrdinalIgnoreCase) || (name.Length == 32 && IsHex(name)))
                {
                    remove.Add(name);
                }
            }
        }
        return new WgsWritePlan(
            container.Name, container.ContainerNumber, newNumber, state, blobLength,
            [
                "write a new GUID blob into the container folder",
                $"write container.{newNumber} naming that blob",
                $"rewrite {IndexFileName} (number, size, state {state}, timestamps; ETag untouched)",
                "remove the superseded manifest and blob",
            ],
            remove, AssessWrite());
    }

    /// <summary>
    /// The containers a repair would actually change (an unresolved conflict marker, states outside
    /// the format, manifests whose blob is missing but recoverable). Reads only.
    /// </summary>
    public IReadOnlyList<string> ContainersNeedingRepair()
    {
        var needing = new List<string>();
        if (HasUnresolvedConflicts) needing.Add(CloudConflictLabel);
        foreach (var container in _containers)
        {
            if (NeedsStateRepair(container)) { needing.Add(container.Name); continue; }

            var folder = Path.Combine(_root, container.FolderName);
            try
            {
                var (current, previous) = ReadManifestBlobGuids(folder, container.ContainerNumber);
                if (_fs.FileExists(Path.Combine(folder, BlobFileName(current)))) continue;
                if (previous != current && _fs.FileExists(Path.Combine(folder, BlobFileName(previous))))
                {
                    needing.Add(container.Name);
                    continue;
                }
                if (FindFallbackBlob(folder, current, container.BlobSize) is not null) needing.Add(container.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // An unreadable manifest is not something repair can mend either.
            }
        }
        return needing;
    }

    /// <summary>The label used for the index-level conflict marker in repair lists.</summary>
    public const string CloudConflictLabel = "the unsettled Xbox conflict";

    private static bool NeedsStateRepair(WgsContainer c)
        => c.HasInvalidState || c.State == WgsEntryState.Deleted || c.StateContradictsEtag;

    // ------------------------------------------------------------------------------------
    // Reading blobs
    // ------------------------------------------------------------------------------------

    /// <summary>Reads a container's blob bytes, throwing when it cannot be read safely.</summary>
    public byte[] ReadBlob(WgsContainer container)
    {
        var r = ResolveBlob(container, strictLayout: false);
        if (r.Status != WgsOperationStatus.Ok) throw new InvalidDataException(r.Message);
        return r.Blob!;
    }

    /// <summary>Reads a container's blob and reports a missing blob, an in-flight sync or an unsupported layout as typed results.</summary>
    public WgsReadResult TryReadBlob(WgsContainer container)
    {
        try
        {
            return ResolveBlob(container, strictLayout: true);
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            return new WgsReadResult(WgsOperationStatus.LockConflict, null, ex.Message, false);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return new WgsReadResult(WgsOperationStatus.UnsupportedLayout, null,
                $"The manifest for '{container.Name}' is not in a layout this package understands: {ex.Message}", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WgsReadResult(WgsOperationStatus.Failed, null, ex.Message, false);
        }
    }

    private WgsReadResult ResolveBlob(WgsContainer container, bool strictLayout)
    {
        var folder = Path.Combine(_root, container.FolderName);
        var manifest = ReadManifest(folder, container.ContainerNumber);
        if (strictLayout && manifest.BlobCount != 1)
        {
            return new WgsReadResult(WgsOperationStatus.UnsupportedLayout, null,
                $"'{container.Name}' declares {manifest.BlobCount} blobs; this package models one.", false);
        }
        var blobGuid = manifest.Current;
        var previousGuid = manifest.Previous;
        var blobPath = Path.Combine(folder, BlobFileName(blobGuid));
        var previousPath = Path.Combine(folder, BlobFileName(previousGuid));
        var haveCurrent = _fs.FileExists(blobPath);
        var havePrevious = previousGuid != blobGuid && _fs.FileExists(previousPath);

        // Both ids present and different means a sync is in flight: one is the cloud's copy and one
        // is this machine's, and nothing on disk says which is meant to win.
        if (haveCurrent && havePrevious)
        {
            MarkRecovered(container.Name);
            return new WgsReadResult(WgsOperationStatus.SyncInFlight, null,
                $"'{container.Name}' has two versions of its data on disk ({blobGuid:N} and {previousGuid:N}), "
                + "which means Xbox is part-way through syncing this save. Close the game and the Xbox app, "
                + "wait for syncing to finish, and open it again.", false);
        }

        if (haveCurrent) return new WgsReadResult(WgsOperationStatus.Ok, _fs.ReadAllBytes(blobPath), null, false);

        // The current id is missing but the manifest also names the id the cloud last knew: a
        // recorded alternative rather than a guess.
        if (havePrevious)
        {
            MarkRecovered(container.Name);
            _options.Log.Warn(
                $"Save blob '{blobGuid:N}' for '{container.Name}' is not on disk; using the previous one "
                + $"the manifest names ('{previousGuid:N}'). Xbox has not finished syncing this save.");
            return new WgsReadResult(WgsOperationStatus.Ok, _fs.ReadAllBytes(previousPath), null, true);
        }

        // Last resort: the only other GUID-named blob in the folder, and only when its size matches
        // what the index records for this container.
        var fallback = FindFallbackBlob(folder, blobGuid, container.BlobSize);
        if (fallback is not null)
        {
            MarkRecovered(container.Name);
            _options.Log.Warn(
                $"Save blob '{blobGuid:N}' for '{container.Name}' not found on disk - " +
                $"using existing blob '{Path.GetFileName(fallback)}' as a fallback. " +
                "This means Xbox cloud sync has not finished for this save; reading works but writing " +
                "now risks Xbox discarding the change. The save was read successfully.");
            return new WgsReadResult(WgsOperationStatus.Ok, _fs.ReadAllBytes(fallback), null, true);
        }

        return new WgsReadResult(WgsOperationStatus.MissingBlob, null,
            $"Save data blob for '{container.Name}' is missing (expected {blobGuid:N}). " +
            "Xbox cloud sync may not have finished downloading this save - " +
            "close the game completely, wait for sync to complete, and try again.", false);
    }

    private string? FindFallbackBlob(string folder, Guid expectedGuid, long expectedSize)
    {
        var expected = BlobFileName(expectedGuid);
        var all = new List<string>();
        var sizeMatch = new List<string>();
        foreach (var file in _fs.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("container.", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Length != 32 || !IsHex(name)) continue;
            all.Add(file);
            if (expectedSize > 0 && _fs.GetFileLength(file) == expectedSize) sizeMatch.Add(file);
        }
        if (sizeMatch.Count == 1) return sizeMatch[0];
        if (expectedSize > 0) return null;
        return all.Count == 1 ? all[0] : null;
    }

    // ------------------------------------------------------------------------------------
    // Repair (explicit; previewed by ContainersNeedingRepair)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Repairs what <see cref="ContainersNeedingRepair"/> lists: clears the conflict marker, puts
    /// undefined container states back to a real one, and points manifests at the blob actually on
    /// disk. Never repairs save data. Returns the names repaired. Never runs as a side effect of a read.
    /// </summary>
    public IReadOnlyList<string> RepairRecoveredManifests()
    {
        var repaired = new List<string>();
        var indexNeedsRewrite = false;

        if (HasUnresolvedConflicts)
        {
            SyncState &= ~WgsSyncState.HasUnresolvedConflicts;
            indexNeedsRewrite = true;
            repaired.Add(CloudConflictLabel);
            _options.Log.Info("Cleared the unresolved-conflict marker; Xbox had left it set with nothing to clear it.");
        }

        foreach (var container in _containers.Where(NeedsStateRepair))
        {
            var fixedState = string.IsNullOrEmpty(container.Etag) ? WgsEntryState.Created : WgsEntryState.Modified;
            _options.Log.Warn(
                $"Container '{container.Name}' carried state {container.RawState}"
                + $"{(container.StateContradictsEtag ? " (which disagrees with its cloud version token)" : "")}"
                + $"; setting it to {fixedState}.");
            container.State = fixedState;
            container.RawState = (uint)fixedState;
            indexNeedsRewrite = true;
            repaired.Add(container.Name);
        }

        foreach (var container in _containers)
        {
            var folder = Path.Combine(_root, container.FolderName);
            Guid expected;
            try { expected = ReadManifestBlobGuid(folder, container.ContainerNumber); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentException) { continue; }

            if (_fs.FileExists(Path.Combine(folder, BlobFileName(expected))))
            {
                _recoveredContainers.Remove(container.Name);
                continue;
            }

            var fallback = FindFallbackBlob(folder, expected, container.BlobSize);
            if (fallback is null) continue;
            var fallbackName = Path.GetFileName(fallback);
            if (!Guid.TryParseExact(fallbackName, "N", out var fallbackGuid)) continue;

            WriteManifest(folder, container.ContainerNumber, fallbackGuid);
            var actualSize = _fs.GetFileLength(fallback);
            if (container.BlobSize != actualSize) { container.BlobSize = actualSize; indexNeedsRewrite = true; }

            repaired.Add(container.Name);
            _recoveredContainers.Remove(container.Name);
            _options.Log.Info(
                $"Repaired container '{container.Name}': container.{container.ContainerNumber} now points at on-disk blob '{fallbackName}'.");
        }

        if (indexNeedsRewrite) WriteIndex();
        return repaired;
    }

    // ------------------------------------------------------------------------------------
    // Orphans
    // ------------------------------------------------------------------------------------

    /// <summary>True when the folder holds GUID sub-folders with a manifest that the index no longer references.</summary>
    public static bool HasOrphanedWorldFolders(string folder, WgsStoreOptions? options = null)
        => FindOrphanedContainers(folder, options).Count > 0;

    /// <summary>
    /// Every GUID sub-folder still holding save data that no index entry points at: what Xbox cloud
    /// sync leaves behind when it drops a container from the index but not from the disk. Best
    /// effort: an unreadable tree yields nothing rather than throwing.
    /// </summary>
    public static IReadOnlyList<WgsOrphanedContainer> FindOrphanedContainers(string folder, WgsStoreOptions? options = null)
    {
        var opts = options ?? WgsStoreOptions.Default;
        var orphans = new List<WgsOrphanedContainer>();
        try
        {
            if (!IsContainerFolder(folder, opts.FileSystem)) return orphans;
            var store = Open(folder, opts);
            var referenced = new HashSet<string>(
                store.Containers.Select(c => c.FolderName), StringComparer.OrdinalIgnoreCase);
            var takenNames = new HashSet<string>(
                store.Containers.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var sub in opts.FileSystem.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(sub);
                if (name.Length != 32 || !IsHex(name)) continue;
                if (referenced.Contains(name)) continue;
                if (store.Describe(sub, name, takenNames) is { } orphan) orphans.Add(orphan);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an unreadable tree as "nothing recoverable detected".
        }
        return orphans;
    }

    public IReadOnlyList<WgsOrphanedContainer> OrphanedContainers() => FindOrphanedContainers(_root, _options);

    /// <summary>
    /// Adds an index entry pointing at an orphaned folder's data. Nothing is copied or rewritten;
    /// only <c>containers.index</c> changes. The entry has no ETag, so it is
    /// <see cref="WgsEntryState.Created"/>: whatever the cloud knew went with the entry that named it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No name could be worked out, or it is already used.</exception>
    public WgsContainer ReRegisterOrphan(WgsOrphanedContainer orphan, string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(orphan);
        EnsureWritable();

        var name = containerName ?? orphan.SuggestedContainerName
            ?? throw new InvalidOperationException("This leftover data does not say which container it belongs to, so it needs a name to be put back.");
        if (Find(name) is not null)
        {
            throw new InvalidOperationException($"This store already has a container called '{name}'.");
        }
        if (!Guid.TryParseExact(orphan.FolderName, "N", out var folderGuid))
        {
            throw new InvalidOperationException($"'{orphan.FolderName}' is not a save data folder.");
        }

        var container = new WgsContainer
        {
            Name = name,
            Name2 = name,
            Etag = string.Empty,
            ContainerNumber = orphan.ContainerNumber,
            State = WgsEntryState.Created,
            RawState = (uint)WgsEntryState.Created,
            FolderGuid = folderGuid,
            FileTime = NowEntryFileTime(),
            Reserved = 0,
            BlobSize = orphan.BlobSize,
        };
        _containers.Add(container);
        WriteIndex();
        _options.Log.Info(
            $"Put leftover save folder '{orphan.FolderName}' back into the container list as '{name}' "
            + $"(container.{orphan.ContainerNumber}, {orphan.BlobSize} bytes).");
        return container;
    }

    private WgsOrphanedContainer? Describe(string sub, string folderName, HashSet<string> takenNames)
    {
        byte number = 0;
        try
        {
            foreach (var manifest in _fs.EnumerateFiles(sub))
            {
                var file = Path.GetFileName(manifest);
                if (!file.StartsWith("container.", StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = file["container.".Length..];
                if (byte.TryParse(suffix, out var n) && n >= number) number = n;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (number == 0) return null;

        Guid blobGuid;
        try { blobGuid = ReadManifestBlobGuid(sub, number); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException) { return null; }

        var blobPath = Path.Combine(sub, BlobFileName(blobGuid));
        if (!_fs.FileExists(blobPath))
        {
            var fallback = FindFallbackBlob(sub, blobGuid, expectedSize: 0);
            if (fallback is null) return null;
            blobPath = fallback;
            if (Guid.TryParseExact(Path.GetFileName(fallback), "N", out var recovered)) blobGuid = recovered;
        }

        long size;
        DateTime written;
        try
        {
            size = _fs.GetFileLength(blobPath);
            written = _fs.GetLastWriteTimeUtc(blobPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        WgsBlobDescription? description = null;
        if (_options.BlobInspector is { } inspector)
        {
            try
            {
                description = inspector.Inspect(_fs.ReadHead(blobPath, inspector.HeadBytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or ArgumentOutOfRangeException)
            {
                // A blob that cannot be read simply has no name to offer.
            }
        }
        var suggested = description?.SuggestedContainerName;
        if (suggested is not null && takenNames.Contains(suggested))
        {
            // The live container of that name is still in the index, so this is an older copy.
            suggested = null;
        }
        return new WgsOrphanedContainer(folderName, sub, number, blobGuid, size, written, description?.Label, suggested);
    }

    // ------------------------------------------------------------------------------------
    // Writing
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Writes new blob bytes for a container: a fresh GUID blob, a new <c>container.&lt;N+1&gt;</c>
    /// manifest, an updated index entry, then removal of the superseded generation. The ETag is left
    /// alone (only the service may issue one). Throws when the write gate refuses.
    /// </summary>
    public void WriteBlob(WgsContainer container, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(blob);
        EnsureWritable();
        var folder = Path.Combine(_root, container.FolderName);
        _fs.CreateDirectory(folder);

        var newBlobGuid = Guid.NewGuid();
        _fs.WriteAllBytes(Path.Combine(folder, BlobFileName(newBlobGuid)), blob);

        var newNumber = unchecked((byte)(container.ContainerNumber + 1));
        WriteManifest(folder, newNumber, newBlobGuid);

        container.ContainerNumber = newNumber;
        container.BlobSize = blob.Length;
        container.FileTime = NowEntryFileTime();

        // A container that has never been uploaded has no ETag and stays Created, because the state
        // and the ETag have to keep agreeing (see WgsContainer.StateContradictsEtag).
        container.State = string.IsNullOrEmpty(container.Etag) ? WgsEntryState.Created : WgsEntryState.Modified;
        container.RawState = (uint)container.State;

        WriteIndex();
        PruneSupersededGenerations(folder, newNumber, newBlobGuid);
        _options.Log.Info($"wgs: wrote container '{container.Name}' as {container.State}, container.{newNumber} ({blob.Length} bytes).");
    }

    /// <summary>
    /// Like <see cref="WriteBlob"/> but reports a refusal, a lock conflict or a concurrent change as a
    /// typed result. A concurrent change is detected BEFORE anything is written, by comparing the
    /// index on disk with the one this instance last read or wrote.
    /// </summary>
    public WgsCommitResult TryWriteBlob(WgsContainer container, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(blob);
        return Commit(container.Name, () => { WriteBlob(container, blob); return container; });
    }

    /// <summary>Typed-result form of <see cref="AddOrReplaceContainer"/>.</summary>
    public WgsCommitResult TryAddOrReplaceContainer(string containerName, byte[] blob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(blob);
        return Commit(containerName, () => { AddOrReplaceContainer(containerName, blob); return Find(containerName)!; });
    }

    private WgsCommitResult Commit(string containerName, Func<WgsContainer> write)
    {
        var assessment = AssessWrite();
        if (!assessment.CanWrite)
        {
            return new WgsCommitResult(WgsOperationStatus.Refused, null, assessment.BlockingMessage(), assessment);
        }
        var change = DetectExternalChange();
        if (change.Changed)
        {
            return new WgsCommitResult(WgsOperationStatus.ConcurrentChange, null,
                string.Join(" ", change.Reasons) + " Re-open the store and re-evaluate before writing.", assessment);
        }
        try
        {
            return new WgsCommitResult(WgsOperationStatus.Ok, write(), null, assessment);
        }
        catch (WgsWriteRefusedException ex)
        {
            return new WgsCommitResult(WgsOperationStatus.Refused, null, ex.Message, ex.Assessment);
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            return new WgsCommitResult(WgsOperationStatus.LockConflict, null,
                $"'{containerName}' could not be written because a file is in use or access was denied: {ex.Message}", assessment);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new WgsCommitResult(WgsOperationStatus.Failed, null, ex.Message, assessment);
        }
    }

    /// <summary>
    /// Adds a container, or replaces the blob of one that exists, preserving the index and every
    /// other container.
    /// </summary>
    public void AddOrReplaceContainer(string containerName, byte[] blob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(blob);

        if (Find(containerName) is { } existing)
        {
            WriteBlob(existing, blob);
            return;
        }

        EnsureWritable();
        var folderGuid = Guid.NewGuid();
        var folder = Path.Combine(_root, folderGuid.ToString("N").ToUpperInvariant());
        _fs.CreateDirectory(folder);
        var blobGuid = Guid.NewGuid();
        _fs.WriteAllBytes(Path.Combine(folder, BlobFileName(blobGuid)), blob);
        WriteManifest(folder, 1, blobGuid);

        _containers.Add(new WgsContainer
        {
            Name = containerName,
            Name2 = containerName,
            // No ETag: the service has never seen this container. It issues one on first upload.
            Etag = string.Empty,
            ContainerNumber = 1,
            State = WgsEntryState.Created,
            RawState = (uint)WgsEntryState.Created,
            FolderGuid = folderGuid,
            FileTime = NowEntryFileTime(),
            Reserved = 0,
            BlobSize = blob.Length,
        });
        WriteIndex();
        _options.Log.Info($"wgs: added container '{containerName}' to {_root} ({blob.Length} bytes).");
    }

    /// <summary>
    /// Creates a brand-new single-container store at <paramref name="destFolder"/>. Refuses a folder
    /// that already holds a <c>containers.index</c>, since the index it writes describes one
    /// container and would orphan the rest.
    /// </summary>
    /// <param name="packageFamilyName">Recorded in the index to identify the owning title.</param>
    public static void WriteNewContainer(string destFolder, string containerName, byte[] blob,
        string packageFamilyName, WgsStoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(packageFamilyName);
        var opts = options ?? WgsStoreOptions.Default;
        var fs = opts.FileSystem;
        if (IsContainerFolder(destFolder, fs))
        {
            throw new InvalidOperationException(
                $"'{destFolder}' is already an Xbox save folder. Writing a new container list here would "
                + "orphan the saves already in it. Choose an empty folder, or merge into this one instead.");
        }
        fs.CreateDirectory(destFolder);

        var folderGuid = Guid.NewGuid();
        var folder = Path.Combine(destFolder, folderGuid.ToString("N").ToUpperInvariant());
        fs.CreateDirectory(folder);

        var blobGuid = Guid.NewGuid();
        fs.WriteAllBytes(Path.Combine(folder, BlobFileName(blobGuid)), blob);
        WriteManifest(fs, folder, 1, blobGuid);

        var now = NowEntryFileTime(opts.Clock);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        w.Write(KnownIndexVersion);
        w.Write(1u);                               // container count
        w.Write(0u);                               // reserved
        WriteWString(w, packageFamilyName);
        w.Write(opts.Clock.UtcNow.ToFileTime());   // index FILETIME (full precision, as the game writes)
        w.Write((uint)WgsSyncState.None);          // never uploaded or downloaded, no conflict to inherit
        WriteWString(w, Guid.NewGuid().ToString());// root GUID
        w.Write(new byte[] { 0, 0, 0, 0x10, 0, 0, 0, 0 }); // 8 reserved bytes (as the game writes)
        WriteWString(w, containerName);
        WriteWString(w, containerName);
        WriteWString(w, string.Empty);             // no ETag until the service issues one
        w.Write((byte)1);                          // container number -> container.1
        w.Write((uint)WgsEntryState.Created);
        w.Write(folderGuid.ToByteArray());
        w.Write(now);                              // entry FILETIME
        w.Write(0L);                               // reserved
        w.Write((long)blob.Length);
        w.Flush();
        WriteFileAtomic(fs, Path.Combine(destFolder, IndexFileName), ms.ToArray());
        opts.Log.Info($"Created wgs container '{containerName}' at {destFolder} ({blob.Length} bytes).");
    }

    /// <summary>Copies the whole store folder to <paramref name="destination"/> (the rollback for a write).</summary>
    public void CopyStoreTo(string destination)
    {
        CopyDirectory(_fs, _root, destination);
    }

    private static void CopyDirectory(IWgsFileSystem fs, string source, string destination)
    {
        fs.CreateDirectory(destination);
        foreach (var file in fs.EnumerateFiles(source))
        {
            fs.WriteAllBytes(Path.Combine(destination, Path.GetFileName(file)), fs.ReadAllBytes(file));
        }
        foreach (var dir in fs.EnumerateDirectories(source))
        {
            CopyDirectory(fs, dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    private void PruneSupersededGenerations(string folder, byte keepNumber, Guid keepBlob)
    {
        var keepBlobName = BlobFileName(keepBlob);
        var keepManifest = $"container.{keepNumber}";
        try
        {
            foreach (var file in _fs.EnumerateFiles(folder).ToList())
            {
                var name = Path.GetFileName(file);
                var isManifest = name.StartsWith("container.", StringComparison.OrdinalIgnoreCase);
                var isBlob = name.Length == 32 && IsHex(name);
                if (!isManifest && !isBlob) continue;
                if (isManifest && name.Equals(keepManifest, StringComparison.OrdinalIgnoreCase)) continue;
                if (isBlob && name.Equals(keepBlobName, StringComparison.OrdinalIgnoreCase)) continue;

                try { _fs.DeleteFile(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _options.Log.Warn($"Could not remove superseded save file '{name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _options.Log.Warn($"Could not tidy container folder '{folder}': {ex.Message}");
        }
    }

    // ------------------------------------------------------------------------------------
    // Manifest + index encoding
    // ------------------------------------------------------------------------------------

    private long NowEntryFileTime() => NowEntryFileTime(_options.Clock);

    /// <summary>The current time as the game stamps a container entry: a FILETIME truncated to whole milliseconds.</summary>
    private static long NowEntryFileTime(IWgsClock clock)
    {
        const long TicksPerMillisecond = 10_000;
        var now = clock.UtcNow.ToFileTime();
        return now - (now % TicksPerMillisecond);
    }

    private void MarkRecovered(string name)
    {
        if (!_recoveredContainers.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _recoveredContainers.Add(name);
        }
    }

    private Guid ReadManifestBlobGuid(string folder, byte number) => ReadManifestBlobGuids(folder, number).Current;

    private (Guid Current, Guid Previous) ReadManifestBlobGuids(string folder, byte number)
    {
        var m = ReadManifest(folder, number);
        return (m.Current, m.Previous);
    }

    /// <summary>
    /// Both blob ids a <c>container.N</c> manifest records. The first names the blob as the cloud
    /// last knew it, the second the file on disk; they differ only while a sync is in flight.
    /// </summary>
    private WgsManifestInfo ReadManifest(string folder, byte number)
    {
        var path = Path.Combine(folder, $"container.{number}");
        var d = _fs.ReadAllBytes(path);
        var pos = 0;
        ReadU32(d, ref pos);                 // constant (4)
        var blobCount = ReadU32(d, ref pos);
        if (blobCount < 1) throw new InvalidDataException($"{path} declares no blobs.");
        pos += BlobNameFieldBytes;           // fixed "Data" name field
        var previous = new Guid(d.AsSpan(pos, 16).ToArray());
        var current = new Guid(d.AsSpan(pos + 16, 16).ToArray());
        return new WgsManifestInfo(current, previous, blobCount);
    }

    private void WriteManifest(string folder, byte number, Guid blobGuid) => WriteManifest(_fs, folder, number, blobGuid);

    private static void WriteManifest(IWgsFileSystem fs, string folder, byte number, Guid blobGuid)
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
        WriteFileAtomic(fs, Path.Combine(folder, $"container.{number}"), ms.ToArray());
    }

    /// <summary>
    /// Writes through a sibling temp file and an atomic same-volume replace, so an interrupted write
    /// never leaves a half-written file. The index is the one file the whole store hangs off.
    /// </summary>
    private static void WriteFileAtomic(IWgsFileSystem fs, string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        try
        {
            fs.WriteAllBytes(temp, bytes);
            fs.MoveOverwrite(temp, path);
        }
        catch
        {
            try { if (fs.FileExists(temp)) fs.DeleteFile(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            throw;
        }
    }

    private void WriteIndex()
    {
        // Refresh the two header fields the game itself rewrites on every save, so the index reads as
        // a legitimately newer version to cloud sync: the container count and the index FILETIME.
        BitConverter.GetBytes((uint)_containers.Count).CopyTo(_header, 4);
        var fileTimeOffset = _indexFileTimeOffset;
        if (fileTimeOffset > 0 && fileTimeOffset + 8 <= _header.Length)
        {
            // Strictly advance the timestamp: cloud sync compares it to decide which copy is newer,
            // so it must never read as same-or-older than the version already on disk.
            var previous = BitConverter.ToInt64(_header, fileTimeOffset);
            var now = _options.Clock.UtcNow.ToFileTime();
            var stamp = now > previous ? now : previous + 1;
            BitConverter.GetBytes(stamp).CopyTo(_header, fileTimeOffset);
            IndexFileTime = stamp;
        }
        else
        {
            throw new InvalidDataException(
                "This Xbox save's container list is too short to carry its last-modified time, so an "
                + "edit could not be marked as newer than the cloud copy. The file looks damaged; "
                + "restore it from a backup or let the game rewrite it before editing.");
        }

        // The store now holds something the cloud does not, so it is no longer fully uploaded. The
        // conflict bit is NOT touched here: only an explicit repair may clear it.
        SyncState &= ~WgsSyncState.FullyUploaded;
        if (_syncFlagsOffset + 4 <= _header.Length)
        {
            BitConverter.GetBytes((uint)SyncState).CopyTo(_header, _syncFlagsOffset);
        }

        using var ms = new MemoryStream();
        ms.Write(_header, 0, _header.Length);
        using var w = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        foreach (var c in _containers)
        {
            WriteWString(w, c.Name);
            WriteWString(w, c.Name2);
            WriteWString(w, c.Etag);
            w.Write(c.ContainerNumber);
            w.Write((uint)c.State);
            w.Write(c.FolderGuid.ToByteArray());
            w.Write(c.FileTime);
            w.Write(c.Reserved);
            w.Write(c.BlobSize);
        }
        w.Flush();
        var bytes = ms.ToArray();
        WriteFileAtomic(_fs, Path.Combine(_root, IndexFileName), bytes);
        _indexFingerprint = Fingerprint(bytes);
    }

    // ------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// True for the failures that mean "another process has this file": a Windows sharing or lock
    /// violation, or access denied. Adapters and tests can raise the same shapes.
    /// </summary>
    public static bool IsLockConflict(Exception ex)
        => ex is UnauthorizedAccessException
            || (ex is IOException io && ((io.HResult & 0xFFFF) is 32 or 33));

    private static string BlobFileName(Guid g) => g.ToString("N").ToUpperInvariant();

    private static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        }
        return true;
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
