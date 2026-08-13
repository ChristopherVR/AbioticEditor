using System.Text;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// How Xbox Connected Storage tracks one container against its cloud copy.
/// </summary>
/// <remarks>
/// <para>Two reverse-engineering lineages disagree about 2, 4 and 5, and picking the wrong one
/// writes a state that means something else entirely. This mapping follows libNOM.io (the engine
/// behind the mainstream No Man's Sky editor) and is the one the evidence supports:</para>
/// <list type="bullet">
///   <item>Across a real Abiotic Factor save and every backup of a live one, containers written by
///     the game itself are only ever 1 or 2 - never 4 or 5 - and always carry an ETag.</item>
///   <item>Two independently written parsers (palworld-xgp-import, palworld-save-pal) reject any
///     entry where <c>state &amp; 4</c> disagrees with "the ETag is empty". So bit 2 means
///     local-only-never-uploaded, which 4 and 5 both are, and 2 cannot mean that.</item>
/// </list>
/// <para>The competing mapping (LukeFZ/XblContainerReader) calls 5 "Modified" and 2 "Unknown".
/// Following it would have this editor stamp every edit as a container the cloud has never heard
/// of while leaving an ETag on it - a combination no other tool produces, that those two parsers
/// treat as corrupt, and that the Palworld tools found the sync engine silently discards.</para>
/// </remarks>
public enum WgsEntryState : uint
{
    UnknownZero = 0,

    /// <summary>Local and cloud agree. Where a container rests after a completed sync.</summary>
    Synced = 1,

    /// <summary>Changed locally since the last sync, and still based on a known cloud version -
    /// what a save this editor has just rewritten is. Keeps its ETag.</summary>
    Modified = 2,

    /// <summary>A tombstone. The entry stays in the index so the deletion can reach the cloud;
    /// a container left in this state is one the service is entitled to take away.</summary>
    Deleted = 3,

    UnknownFour = 4,

    /// <summary>Made locally and never uploaded, so there is no cloud version to name and the
    /// ETag is empty.</summary>
    Created = 5,
}

/// <summary>
/// Index-level sync flags (LibXblContainer <c>ContainerSyncFlags</c>). Bit 4 is the one that
/// matters here: a store carrying it has a conflict Xbox has not resolved, and editing into that
/// is how an edit gets thrown away.
/// </summary>
[Flags]
public enum WgsSyncState : uint
{
    None = 0,
    FullyUploaded = 1 << 0,
    FullyDownloaded = 1 << 1,
    HasUnresolvedConflicts = 1 << 4,
}

/// <summary>One logical container in an Xbox "wgs" (Connected Storage) folder.</summary>
public sealed class WgsContainer
{
    public required string Name { get; init; }
    public required string Name2 { get; init; }

    /// <summary>
    /// The container's ETag: a version token issued by the Xbox service, not by this machine.
    /// It is echoed back untouched on every local write - the service uses it to recognise which
    /// cloud version the local copy was based on. Generating a fresh one locally claims a version
    /// the service never issued, which is exactly how an upload stops matching.
    /// </summary>
    public required string Etag { get; set; }

    public byte ContainerNumber { get; set; }

    /// <summary>
    /// Where this container stands against its cloud copy. A save just rewritten is
    /// <see cref="WgsEntryState.Modified"/>; one that has never been uploaded is
    /// <see cref="WgsEntryState.Created"/>.
    /// </summary>
    public WgsEntryState State { get; set; } = WgsEntryState.Synced;

    /// <summary>The raw state as read, so a value outside <see cref="WgsEntryState"/> can be
    /// reported and repaired rather than silently reinterpreted.</summary>
    public uint RawState { get; set; } = (uint)WgsEntryState.Synced;

    /// <summary>True when the index carried a state this format does not define (anything above
    /// <see cref="WgsEntryState.Created"/>).</summary>
    public bool HasInvalidState => RawState > (uint)WgsEntryState.Created;

    /// <summary>
    /// True when the state and the ETag contradict each other. Bit 2 of the state means "local
    /// only, never uploaded", which is exactly the case where there is no cloud version to name,
    /// so it must be set if and only if the ETag is empty. Two independently written parsers
    /// reject an entry that breaks this, so producing one risks a save other tools - and plausibly
    /// Xbox itself - treat as damaged.
    /// </summary>
    public bool StateContradictsEtag
        => ((RawState & 4) != 0) != string.IsNullOrEmpty(Etag);

    public required Guid FolderGuid { get; init; }
    public long FileTime { get; set; }
    public long Reserved { get; set; }
    public long BlobSize { get; set; }

    public string FolderName => FolderGuid.ToString("N").ToUpperInvariant();
}

/// <summary>
/// Save data sitting in a wgs folder that the container list no longer points at - what Xbox cloud
/// sync leaves behind when it drops a world from the index but not from the disk.
/// </summary>
/// <param name="FolderName">The GUID folder name (32 hex characters), as it appears on disk.</param>
/// <param name="FolderPath">The full path to that folder.</param>
/// <param name="ContainerNumber">The generation its newest <c>container.N</c> manifest describes.</param>
/// <param name="BlobId">The data blob that manifest points at (or the one found beside it).</param>
/// <param name="BlobSize">That blob's size in bytes - the quickest way to tell two leftovers apart.</param>
/// <param name="LastWrittenUtc">When the data was last written.</param>
/// <param name="WorldName">The world name read out of the data, or null when it is not a world.</param>
/// <param name="SuggestedContainerName">The name to put it back under, or null when none can be worked out.</param>
public sealed record WgsOrphanedContainer(
    string FolderName,
    string FolderPath,
    byte ContainerNumber,
    Guid BlobId,
    long BlobSize,
    DateTime LastWrittenUtc,
    string? WorldName,
    string? SuggestedContainerName);

/// <summary>
/// Reads and writes an Xbox "wgs" (Windows Game Saves / Connected Storage) folder - the on-disk
/// shape a Game Pass / Microsoft Store title uses instead of loose <c>.sav</c> files. The folder
/// holds a <c>containers.index</c> mapping logical container names to GUID sub-folders; each
/// sub-folder has a <c>container.N</c> manifest pointing at a GUID-named blob file (the actual
/// payload). See the project memory "Game Pass save format" for the byte layout.
///
/// <para>Writing a blob follows the game's own scheme: a fresh GUID blob is written, a new
/// <c>container.&lt;N+1&gt;</c> points at it, and the index entry is updated (number bumped, state
/// moved to <see cref="WgsEntryState.Modified"/>, size, timestamp), then the superseded generation
/// is removed so the folder keeps the one-manifest-one-blob shape the game itself maintains. The
/// whole folder is backed up first (callers use <see cref="GamePassSaveSet"/>, which does that),
/// and that backup is the rollback.</para>
///
/// <para>This folder is one half of a conversation with the Xbox cloud, not a private file format.
/// The index records what the service should believe about each container - whether it matches the
/// cloud (<see cref="WgsContainer.State"/>) and which cloud version it was based on
/// (<see cref="WgsContainer.Etag"/>). Getting those wrong does not fail loudly; it loses an
/// argument later, out of sight, and takes the edit with it. So state is set to what actually
/// happened, the ETag is echoed rather than invented, and anything whose meaning is not established
/// is round-tripped verbatim.</para>
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

    private List<WgsContainer> _containers = new();

    public IReadOnlyList<WgsContainer> Containers => _containers;

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

    /// <summary>The index-level FILETIME recorded in the header - the "last modified" recency token Xbox
    /// cloud sync compares to decide which copy (local vs cloud) is newer. The game advances it on every
    /// save; so does this editor (see <see cref="WriteIndex"/>).</summary>
    public long IndexFileTime { get; private set; }

    /// <summary>Index-level sync state (see <see cref="WgsSyncState"/>).</summary>
    public WgsSyncState SyncState { get; private set; }

    private int _syncFlagsOffset;
    private int _indexFileTimeOffset;

    /// <summary>
    /// True when Xbox has a conflict for this save that it has not resolved. Writing into a store
    /// in this state is not a normal edit: the service already believes local and cloud disagree,
    /// so whatever is written here is one side of an argument that gets settled later, out of
    /// sight, and can be settled against you.
    /// </summary>
    public bool HasUnresolvedConflicts => SyncState.HasFlag(WgsSyncState.HasUnresolvedConflicts);

    /// <summary>
    /// Containers whose recorded state is not a value the format defines. Only this editor is
    /// known to have produced them: it used to treat the state field as a write counter and
    /// increment it, walking containers through Deleted(3) and Created(4) and out the far end to
    /// 6, 7 and beyond. Reported so they can be put back to a real state.
    /// </summary>
    public IReadOnlyList<string> InvalidStateContainers
        => _containers.Where(c => c.HasInvalidState || c.StateContradictsEtag).Select(c => c.Name).ToList();

    /// <summary>
    /// Containers a write must not be allowed to build on: their state is either outside the format
    /// entirely, or a deletion tombstone. Both describe a container the service is entitled to take
    /// away, so an edit written into one can vanish with it.
    /// </summary>
    public IReadOnlyList<string> UnsafeStateContainers
        => _containers.Where(c => c.HasInvalidState || c.State == WgsEntryState.Deleted)
            .Select(c => c.Name).ToList();

    /// <summary>
    /// Containers whose state and cloud version token contradict each other. Writing the container
    /// puts it right (see <see cref="WriteBlob"/>), so this is reported rather than refused.
    /// </summary>
    public IReadOnlyList<string> ContradictoryStateContainers
        => _containers.Where(c => !c.HasInvalidState && c.StateContradictsEtag).Select(c => c.Name).ToList();

    /// <summary>
    /// Accepted risk that lets writes proceed into a store this editor would otherwise refuse
    /// (see <see cref="AllowUnsafeWrites"/>). Null until a caller sets it deliberately.
    /// </summary>
    public GamePassWriteOverride? WriteOverride { get; private set; }

    /// <summary>
    /// Everything known about whether writing to this store now would survive: the index's own
    /// conflict flag, container states, and what is running on this machine.
    /// </summary>
    public GamePassWriteCheck CheckWritable()
        => GamePassWriteCheck.For(
            HasUnresolvedConflicts,
            UnsafeStateContainers,
            ContradictoryStateContainers,
            GamePassEnvironment.Scan(),
            GamePassEnvironment.IsInsideConnectedStorage(_root));

    /// <summary>
    /// Records that a caller has accepted the risk described by <see cref="CheckWritable"/> and
    /// wants the write to happen anyway. Applies to this store instance only, so accepting the risk
    /// once never carries over to the next save the editor opens.
    /// </summary>
    /// <param name="acknowledgement">The accepted risk (see <see cref="GamePassWriteOverride"/>).</param>
    public void AllowUnsafeWrites(GamePassWriteOverride acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        WriteOverride = acknowledgement;
        Diagnostics.EditorLog.Warn("GamePass",
            $"Writes to '{_root}' were allowed past the safety check: {acknowledgement.Reason}");
    }

    /// <summary>
    /// Refuses the write when this save is in no state to take one, unless a caller has explicitly
    /// accepted the risk (<see cref="AllowUnsafeWrites"/>). Every write path calls this before it
    /// touches anything, including before taking the backup, so a refused save leaves the folder
    /// exactly as it was.
    /// </summary>
    /// <exception cref="GamePassUnsafeWriteException">The save is not safe to write and no risk was accepted.</exception>
    public void EnsureWritable()
    {
        var check = CheckWritable();
        if (check.CanWrite) return;
        if (WriteOverride is not null)
        {
            Diagnostics.EditorLog.Warn("GamePass",
                $"Writing to '{_root}' despite: {check.BlockingMessage()} (accepted: {WriteOverride.Reason})");
            return;
        }
        throw new GamePassUnsafeWriteException(check);
    }

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
        // Record where the timestamp actually sits rather than recomputing it later from the name's
        // length. What is read above as "reserved" is really an empty length-prefixed display name,
        // so a save that ever carries a non-empty one would push this field along and a computed
        // offset would land in the middle of a string.
        _indexFileTimeOffset = pos;
        IndexFileTime = ReadI64(d, ref pos); // index-level FILETIME (the recency token sync compares)
        _syncFlagsOffset = pos;
        SyncState = (WgsSyncState)ReadU32(d, ref pos);
        ReadWString(d, ref pos);            // root GUID string
        pos += 8;                           // 8 reserved bytes

        _header = d[..pos];

        var list = new List<WgsContainer>((int)count);
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
        => FindOrphanedContainers(folder).Count > 0;

    /// <summary>
    /// Every GUID sub-folder of <paramref name="folder"/> that still holds save data no
    /// <c>containers.index</c> entry points at, with enough detail to tell one from another: which
    /// folder it is, which generation, how big its data is, when it was written, and the world name
    /// read out of the data itself where that is possible.
    ///
    /// <para>This is the shape Xbox cloud sync leaves behind when it drops a world from the index:
    /// the entry goes, the data stays. Listing them is what turns "my world disappeared" into
    /// something a player can put back (<see cref="ReRegisterOrphan"/>).</para>
    ///
    /// <para>Best effort throughout: an unreadable folder contributes nothing rather than throwing.</para>
    /// </summary>
    public static IReadOnlyList<WgsOrphanedContainer> FindOrphanedContainers(string folder)
    {
        var orphans = new List<WgsOrphanedContainer>();
        try
        {
            if (!IsContainerFolder(folder)) return orphans;
            var store = Open(folder);
            var referenced = new HashSet<string>(
                store.Containers.Select(c => c.FolderName), StringComparer.OrdinalIgnoreCase);
            var takenNames = new HashSet<string>(
                store.Containers.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                var name = Path.GetFileName(sub);
                // GUID "N" sub-folders are 32 hex chars; skip anything else (and referenced ones).
                if (name.Length != 32 || !IsHex(name)) continue;
                if (referenced.Contains(name)) continue;
                if (Describe(sub, name, takenNames) is { } orphan) orphans.Add(orphan);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an unreadable tree as "nothing recoverable detected".
        }
        return orphans;
    }

    /// <summary>Orphaned container folders in this store (see <see cref="FindOrphanedContainers"/>).</summary>
    public IReadOnlyList<WgsOrphanedContainer> OrphanedContainers() => FindOrphanedContainers(_root);

    /// <summary>
    /// Turns one orphaned folder back into a container the game can see, by adding an index entry
    /// that points at the data already on disk. Nothing is copied or rewritten: the save data is
    /// left exactly where it is, and only <c>containers.index</c> changes.
    ///
    /// <para>The new entry carries no ETag and so is <see cref="WgsEntryState.Created"/>: whatever
    /// the cloud once knew about this container went with the index entry that named it, and
    /// inventing a version token the service never issued is how an upload stops matching.</para>
    /// </summary>
    /// <param name="orphan">One of the entries from <see cref="OrphanedContainers"/>.</param>
    /// <param name="containerName">
    /// The logical name to give it (for instance <c>MyWorld-WC</c>). Defaults to the orphan's
    /// suggested name, which comes from the world name inside its own data.
    /// </param>
    /// <returns>The container as it now appears in the index.</returns>
    /// <exception cref="InvalidOperationException">The name is already used, or no name could be worked out.</exception>
    public WgsContainer ReRegisterOrphan(WgsOrphanedContainer orphan, string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(orphan);
        EnsureWritable();

        var name = containerName ?? orphan.SuggestedContainerName
            ?? throw new InvalidOperationException(
                "This leftover save does not say which world it belongs to, so it needs a name to be "
                + "put back. Choose one ending in '-WC' to match the world it came from.");
        if (Find(name) is not null)
        {
            throw new InvalidOperationException(
                $"This save already has a world called '{name}'. Put the leftover back under a different "
                + "name, or rename the world that is in the way first.");
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
        Diagnostics.EditorLog.Info("GamePass",
            $"Put leftover save folder '{orphan.FolderName}' back into the container list as '{name}' "
            + $"(container.{orphan.ContainerNumber}, {orphan.BlobSize} bytes).");
        return container;
    }

    /// <summary>
    /// Works out what an unreferenced GUID folder holds. Returns null when it holds nothing usable
    /// (no manifest, or a manifest whose blob is gone), because offering to restore an empty folder
    /// would just move the disappointment further along.
    /// </summary>
    private static WgsOrphanedContainer? Describe(string sub, string folderName, HashSet<string> takenNames)
    {
        byte number = 0;
        try
        {
            foreach (var manifest in Directory.EnumerateFiles(sub, "container.*"))
            {
                var suffix = Path.GetFileName(manifest)["container.".Length..];
                if (byte.TryParse(suffix, out var n) && n >= number) number = n;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (number == 0) return null;

        Guid blobGuid;
        try { blobGuid = ReadManifestBlobGuid(sub, number); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException) { return null; }

        var blobPath = Path.Combine(sub, blobGuid.ToString("N").ToUpperInvariant());
        if (!File.Exists(blobPath))
        {
            // The manifest's own blob is missing, but the folder may still hold exactly one other,
            // which is the same recorded-size recovery the read path uses.
            var fallback = FindFallbackBlob(sub, blobGuid, expectedSize: 0);
            if (fallback is null) return null;
            blobPath = fallback;
            if (Guid.TryParseExact(Path.GetFileName(fallback), "N", out var recovered)) blobGuid = recovered;
        }

        long size;
        DateTime written;
        try
        {
            var info = new FileInfo(blobPath);
            size = info.Length;
            written = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        var world = ReadWorldNameFromBundle(blobPath);
        var suggested = world is null ? null : $"{world}-WC";
        if (suggested is not null && takenNames.Contains(suggested))
        {
            // The live world of that name is still in the index, so this is an older copy of it
            // rather than the missing one. Suggesting the taken name would only be refused later.
            suggested = null;
        }

        return new WgsOrphanedContainer(
            folderName, sub, number, blobGuid, size, written, world, suggested);
    }

    /// <summary>
    /// Reads the world name out of a world bundle by looking at its table of contents only.
    ///
    /// <para>The TOC (member paths, sizes and save classes) sits uncompressed at the front of the
    /// blob; only the member bodies go through Oodle. That matters here: identifying a leftover
    /// world has to work on a machine with no Oodle library at all, which is exactly the machine
    /// where a player is least able to check for themselves what they lost.</para>
    ///
    /// <para>Returns null for anything that is not a world bundle (profile containers, settings)
    /// or that cannot be read.</para>
    /// </summary>
    private static string? ReadWorldNameFromBundle(string blobPath)
    {
        try
        {
            // The TOC's first member path is a few hundred bytes in at most; reading the whole of a
            // multi-megabyte world blob to find it would make listing orphans feel broken.
            var head = new byte[8192];
            int read;
            using (var stream = File.OpenRead(blobPath))
            {
                read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            }
            var data = head.AsSpan(0, read);

            var pos = 0;
            if (ReadBundleString(data, ref pos) != "ABF_SAVE_VERSION") return null;
            pos += 16; // version, uncompressed size, opaque field, member count
            if (pos >= data.Length) return null;
            var first = ReadBundleString(data, ref pos);
            if (first is null) return null;

            // Members are recorded as "Profile/Worlds/<World>/WorldSave_..." (or with backslashes).
            var parts = first.Replace('\\', '/').Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("Worlds", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(parts[i + 1]))
                {
                    return parts[i + 1];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or ArgumentOutOfRangeException)
        {
            // A blob that cannot be read simply has no name to offer.
        }
        return null;
    }

    /// <summary>
    /// One UE FString from a bundle header: a positive length counts ASCII bytes, a negative one
    /// counts UTF-16 characters (which is what the engine writes as soon as a world name leaves
    /// ASCII). Returns null rather than throwing when the bytes do not describe a string.
    /// </summary>
    private static string? ReadBundleString(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos + 4 > data.Length) return null;
        var length = BitConverter.ToInt32(data[pos..]);
        pos += 4;
        if (length == 0) return string.Empty;
        if (length > 0)
        {
            if (length > 4096 || pos + length > data.Length) return null;
            var ascii = Encoding.ASCII.GetString(data.Slice(pos, length)).TrimEnd('\0');
            pos += length;
            return ascii;
        }
        if (length == int.MinValue) return null;
        var bytes = -length * 2;
        if (bytes > 8192 || pos + bytes > data.Length) return null;
        var wide = Encoding.Unicode.GetString(data.Slice(pos, bytes)).TrimEnd('\0');
        pos += bytes;
        return wide;
    }

    /// <summary>Reads the blob bytes for a logical container (via its <c>container.N</c> manifest).</summary>
    public byte[] ReadBlob(WgsContainer container)
    {
        var folder = Path.Combine(_root, container.FolderName);
        var (blobGuid, previousGuid) = ReadManifestBlobGuids(folder, container.ContainerNumber);
        var blobPath = Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant());
        var previousPath = Path.Combine(folder, previousGuid.ToString("N").ToUpperInvariant());
        var haveCurrent = File.Exists(blobPath);
        var havePrevious = previousGuid != blobGuid && File.Exists(previousPath);

        // Both ids present and different means a sync is genuinely in flight: one of these is the
        // cloud's copy and one is this machine's, and nothing on disk says which is meant to win.
        // Guessing here would hand back the wrong save as though it were the right one.
        if (haveCurrent && havePrevious)
        {
            MarkRecovered(container.Name);
            throw new InvalidDataException(
                $"'{container.Name}' has two versions of its data on disk ({blobGuid:N} and {previousGuid:N}), "
                + "which means Xbox is part-way through syncing this save. Close the game and the Xbox app, "
                + "wait for syncing to finish, and open it again.");
        }

        if (haveCurrent) return File.ReadAllBytes(blobPath);

        // The current id is missing but the manifest also names the id the cloud last knew. That is
        // a recorded alternative rather than a guess, so it beats scanning the folder.
        if (havePrevious)
        {
            MarkRecovered(container.Name);
            Diagnostics.EditorLog.Warn("GamePass",
                $"Save blob '{blobGuid:N}' for '{container.Name}' is not on disk; using the previous one "
                + $"the manifest names ('{previousGuid:N}'). Xbox has not finished syncing this save.");
            return File.ReadAllBytes(previousPath);
        }

        // Neither id is on disk. Last resort: the only other GUID-named blob in the folder, and
        // only when its size matches what the index records for this container.
        var fallback = FindFallbackBlob(folder, blobGuid, container.BlobSize);
        if (fallback is not null)
        {
            MarkRecovered(container.Name);
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
    /// <summary>
    /// The containers <see cref="RepairRecoveredManifests"/> would actually change, without
    /// changing anything. Reads the index and checks which blobs are on disk; it never
    /// decompresses, so it is cheap enough to run before a save is opened.
    /// </summary>
    /// <remarks>
    /// Offering a repair is only honest when there is something to repair. Asking the question and
    /// then reporting that nothing was fixed teaches people to distrust the prompt, and some of the
    /// things that make a save look unwell here (an unresolved cloud conflict, most of all) are not
    /// something this editor can put right at all.
    /// </remarks>
    public IReadOnlyList<string> ContainersNeedingRepair()
    {
        var needing = new List<string>();
        foreach (var container in _containers)
        {
            if (NeedsStateRepair(container)) { needing.Add(container.Name); continue; }

            var folder = Path.Combine(_root, container.FolderName);
            try
            {
                var (current, previous) = ReadManifestBlobGuids(folder, container.ContainerNumber);
                if (File.Exists(Path.Combine(folder, current.ToString("N").ToUpperInvariant()))) continue;
                if (previous != current
                    && File.Exists(Path.Combine(folder, previous.ToString("N").ToUpperInvariant())))
                {
                    needing.Add(container.Name);
                    continue;
                }
                if (FindFallbackBlob(folder, current, container.BlobSize) is not null) needing.Add(container.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // An unreadable manifest is not something repair can mend either, so it is not
                // reported as repairable.
            }
        }
        return needing;
    }

    private static bool NeedsStateRepair(WgsContainer c)
        => c.HasInvalidState || c.State == WgsEntryState.Deleted || c.StateContradictsEtag;

    public IReadOnlyList<string> RepairRecoveredManifests()
    {
        var repaired = new List<string>();
        var indexNeedsRewrite = false;

        // Put back any container state that is not a value the format defines. Earlier versions of
        // this editor treated the state field as a write counter and incremented it on every save,
        // marching containers through Deleted(3) and Created(4) and then past the end of the range
        // entirely. A container claiming to be deleted, or claiming a state nothing recognises, is
        // one the service and the game are entitled to ignore - which is what a save that "stopped
        // loading after editing" looks like from the outside. Modified is the honest description of
        // a container this editor has written and Xbox has not yet taken.
        foreach (var container in _containers.Where(NeedsStateRepair))
        {
            // Whether the container has an ETag decides what it is allowed to say about itself:
            // with one it is a known cloud item that has changed locally, without one it has never
            // been uploaded at all.
            var fixedState = string.IsNullOrEmpty(container.Etag) ? WgsEntryState.Created : WgsEntryState.Modified;
            Diagnostics.EditorLog.Warn("GamePass",
                $"Container '{container.Name}' carried state {container.RawState}"
                + $"{(container.StateContradictsEtag ? " (which disagrees with its cloud version token)" : "")}"
                + $"; setting it to {fixedState}.");
            container.State = fixedState;
            container.RawState = (uint)fixedState;
            indexNeedsRewrite = true;
            repaired.Add(container.Name);
        }

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
    ///
    /// <para>Only an unambiguous, corroborated candidate is accepted. When the index records a
    /// blob size (the normal case) exactly one file must match it: substituting a
    /// different-sized blob would silently swap in save data from another point in time, and the
    /// caller treats the result as the real save. The "sole candidate, size unknown" fallback is
    /// kept only for the case where the index has no size to check against.</para>
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
        if (expectedSize > 0) return null;
        return all.Count == 1 ? all[0] : null;
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
    /// <c>containers.index</c>.
    ///
    /// <para>Order matters: the blob lands first, then the manifest that names it, then the index
    /// that names the manifest. A crash at any point therefore leaves the previous generation
    /// still fully described, never a manifest pointing at a blob that does not exist.</para>
    ///
    /// <para>Once the index is committed the superseded generation is deleted, because the game
    /// keeps exactly one <c>container.N</c> + one blob per folder and leftovers actively cause
    /// harm: a stray same-folder blob is what makes <see cref="FindFallbackBlob"/> ambiguous, so
    /// hoarding old generations would sabotage the recovery path for a rollback nobody can
    /// perform from here anyway. The whole folder is backed up before the first write
    /// (<see cref="GamePassSaveSet"/>), which is the real rollback.</para>
    /// </summary>
    public void WriteBlob(WgsContainer container, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(blob);
        // Last line of defence: the higher layers check before they back anything up, but every
        // path that changes a player's save ends here, so this is the one place that cannot be
        // routed around by a new caller.
        EnsureWritable();
        var folder = Path.Combine(_root, container.FolderName);
        Directory.CreateDirectory(folder);

        var newBlobGuid = Guid.NewGuid();
        var blobPath = Path.Combine(folder, newBlobGuid.ToString("N").ToUpperInvariant());
        File.WriteAllBytes(blobPath, blob);

        var newNumber = unchecked((byte)(container.ContainerNumber + 1));
        WriteManifest(folder, newNumber, newBlobGuid);

        container.ContainerNumber = newNumber;
        container.BlobSize = blob.Length;
        container.FileTime = NowEntryFileTime();

        // Say what actually happened: this container now differs from the cloud copy, which is what
        // the service reads to decide there is something here worth uploading. Leaving it at Synced
        // would claim the opposite and invite the cloud copy to come back over it.
        //
        // The ETag is deliberately untouched - it names the cloud version this edit was based on,
        // and only the service may issue a new one. A container that has never been uploaded has no
        // ETag and stays Created, because the state and the ETag have to keep agreeing (see
        // WgsContainer.StateContradictsEtag).
        container.State = string.IsNullOrEmpty(container.Etag) ? WgsEntryState.Created : WgsEntryState.Modified;
        container.RawState = (uint)container.State;

        WriteIndex();
        PruneSupersededGenerations(folder, newNumber, newBlobGuid);
        Diagnostics.EditorLog.Info("GamePass",
            $"wgs: wrote container '{container.Name}' as {container.State}, container.{newNumber} ({blob.Length} bytes).");
    }

    /// <summary>
    /// Adds a logical container to this store, or replaces the blob of one that already exists.
    /// This is how a converted world is merged INTO a real Game Pass save folder: the existing
    /// index and every other container in it are preserved, unlike
    /// <see cref="WriteNewContainer"/> which builds a fresh single-container folder.
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
        Directory.CreateDirectory(folder);
        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);
        WriteManifest(folder, 1, blobGuid);

        var now = NowEntryFileTime();
        _containers.Add(new WgsContainer
        {
            Name = containerName,
            Name2 = containerName,
            // No ETag: the service has never seen this container, so there is no cloud version for
            // it to name. It issues one when the container first uploads.
            Etag = string.Empty,
            ContainerNumber = 1,
            State = WgsEntryState.Created,
            RawState = (uint)WgsEntryState.Created,
            FolderGuid = folderGuid,
            FileTime = now,
            Reserved = 0,
            BlobSize = blob.Length,
        });
        WriteIndex();
        Diagnostics.EditorLog.Info("GamePass",
            $"wgs: added container '{containerName}' to {_root} ({blob.Length} bytes).");
    }

    /// <summary>
    /// Deletes the manifests and blobs left over from earlier generations of one container
    /// folder, keeping only the generation just committed. Best-effort: the index already points
    /// at the new generation, so a file that cannot be deleted (locked by the Xbox app) is a
    /// cosmetic leftover, not a failed save.
    /// </summary>
    private static void PruneSupersededGenerations(string folder, byte keepNumber, Guid keepBlob)
    {
        var keepBlobName = keepBlob.ToString("N").ToUpperInvariant();
        var keepManifest = $"container.{keepNumber}";
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder).ToList())
            {
                var name = Path.GetFileName(file);
                var isManifest = name.StartsWith("container.", StringComparison.OrdinalIgnoreCase);
                var isBlob = name.Length == 32 && IsHex(name);
                if (!isManifest && !isBlob) continue;
                if (isManifest && name.Equals(keepManifest, StringComparison.OrdinalIgnoreCase)) continue;
                if (isBlob && name.Equals(keepBlobName, StringComparison.OrdinalIgnoreCase)) continue;

                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diagnostics.EditorLog.Warn("GamePass",
                        $"Could not remove superseded save file '{name}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("GamePass", $"Could not tidy container folder '{folder}': {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a brand-new single-container wgs folder at <paramref name="destFolder"/> holding one
    /// logical container (<paramref name="containerName"/>) whose blob is <paramref name="blob"/>.
    /// Writes <c>containers.index</c>, the GUID container folder, its <c>container.1</c> manifest and
    /// the blob. Used to convert a Steam world into a Game Pass save.
    ///
    /// <para>Refuses to run on a folder that already holds a <c>containers.index</c>. The index it
    /// writes describes exactly one container, so overwriting a real save store's index would
    /// orphan every other world and profile container in it - the folder would still hold the
    /// data, but nothing would reference it. Merge into an existing store with
    /// <see cref="AddOrReplaceContainer"/> instead.</para>
    /// </summary>
    public static void WriteNewContainer(string destFolder, string containerName, byte[] blob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(blob);
        if (IsContainerFolder(destFolder))
        {
            throw new InvalidOperationException(
                $"'{destFolder}' is already an Xbox save folder. Writing a new container list here would "
                + "orphan the saves already in it. Choose an empty folder, or merge into this one instead.");
        }
        Directory.CreateDirectory(destFolder);

        var folderGuid = Guid.NewGuid();
        var folder = Path.Combine(destFolder, folderGuid.ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(folder);

        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);
        WriteManifest(folder, 1, blobGuid);

        var now = NowEntryFileTime();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true);
        w.Write(14u);                              // version
        w.Write(1u);                               // container count
        w.Write(0u);                               // reserved
        WriteWString(w, AbioticPackageFamilyName);
        w.Write(DateTime.UtcNow.ToFileTimeUtc());  // index FILETIME (full precision, as the game writes)
        // Nothing here has ever been uploaded or downloaded, and there is no conflict to inherit.
        w.Write((uint)WgsSyncState.None);
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
        WriteFileAtomic(Path.Combine(destFolder, IndexFileName), ms.ToArray());
        Diagnostics.EditorLog.Info("GamePass", $"Created wgs container '{containerName}' at {destFolder} ({blob.Length} bytes).");
    }


    /// <summary>
    /// The current time as the game stamps a container entry: a FILETIME truncated to whole
    /// milliseconds. Every entry in the reference save divides exactly by 10,000 ticks, so full
    /// 100ns precision is not something the game ever writes here.
    /// </summary>
    private static long NowEntryFileTime()
    {
        const long TicksPerMillisecond = 10_000;
        var now = DateTime.UtcNow.ToFileTimeUtc();
        return now - (now % TicksPerMillisecond);
    }

    private void MarkRecovered(string name)
    {
        if (!_recoveredContainers.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _recoveredContainers.Add(name);
        }
    }

    private static Guid ReadManifestBlobGuid(string folder, byte number)
        => ReadManifestBlobGuids(folder, number).Current;

    /// <summary>
    /// Both blob ids a <c>container.N</c> manifest records. The first names the blob as the cloud
    /// last knew it, the second the file on disk; a settled container has them identical, which is
    /// what this editor writes. They differ while a sync is in flight, and that is the one case
    /// where the older id is a genuine, named alternative rather than a guess.
    /// </summary>
    private static (Guid Current, Guid Previous) ReadManifestBlobGuids(string folder, byte number)
    {
        var path = Path.Combine(folder, $"container.{number}");
        var d = File.ReadAllBytes(path);
        var pos = 0;
        ReadU32(d, ref pos);                 // constant (4)
        var blobCount = ReadU32(d, ref pos); // blob entries (1)
        if (blobCount < 1) throw new InvalidDataException($"{path} declares no blobs.");
        pos += BlobNameFieldBytes;           // fixed "Data" name field
        var previous = new Guid(d.AsSpan(pos, 16).ToArray());
        var current = new Guid(d.AsSpan(pos + 16, 16).ToArray());
        return (current, previous);
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
        WriteFileAtomic(Path.Combine(folder, $"container.{number}"), ms.ToArray());
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> through a sibling temp file and
    /// an atomic same-volume replace, so an interrupted write can never leave a half-written file
    /// behind. The index in particular is the single file the entire save store hangs off: a
    /// truncated one loses every container at once, which no per-save backup can undo.
    /// </summary>
    private static void WriteFileAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            throw;
        }
    }

    private void WriteIndex()
    {
        // Refresh the two header fields the game itself rewrites on every save, so the index reads as
        // a legitimately newer version to Xbox cloud sync instead of an unchanged one it can discard.
        // (1) the container count, recomputed from the live list, and (2) the index-level FILETIME.
        // The FILETIME sits right after the version (4), count (4), reserved (4) and the length-
        // prefixed package-family-name string (4 + len*2).
        BitConverter.GetBytes((uint)_containers.Count).CopyTo(_header, 4);
        var fileTimeOffset = _indexFileTimeOffset;
        if (fileTimeOffset > 0 && fileTimeOffset + 8 <= _header.Length)
        {
            // Strictly advance the index timestamp. Cloud sync compares this value to decide which
            // copy is newer, so it must never read as same-or-older than the version already on disk;
            // clock resolution or skew could otherwise leave it equal (or behind) and lose the
            // conflict, which is what let edits get rolled back to the cloud copy.
            var previous = BitConverter.ToInt64(_header, fileTimeOffset);
            var now = DateTime.UtcNow.ToFileTimeUtc();
            var stamp = now > previous ? now : previous + 1;
            BitConverter.GetBytes(stamp).CopyTo(_header, fileTimeOffset);
            IndexFileTime = stamp;
        }
        else
        {
            // Only reachable on a malformed/truncated header. Without the recency stamp Xbox sync
            // can decide the cloud copy is the newer one and roll the edit back, so refuse rather
            // than write a save that looks stale the moment it lands.
            throw new InvalidDataException(
                "This Xbox save's container list is too short to carry its last-modified time, so an "
                + "edit could not be marked as newer than the cloud copy. The file looks damaged; "
                + "restore it from a backup or let the game rewrite it before editing.");
        }

        // The store now holds something the cloud does not, so it is no longer fully uploaded.
        // Leaving that bit set describes a save that is already safely in the cloud, which would
        // invite the service to treat the cloud copy as authoritative and pull it back over this
        // one. The conflict bit is NOT touched here: only Xbox can decide a conflict is resolved,
        // and clearing it locally would hide a real problem instead of fixing it.
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
        WriteFileAtomic(Path.Combine(_root, IndexFileName), ms.ToArray());
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
