using System.Diagnostics.CodeAnalysis;
using Storage = AbioticEditor.GamePass.Storage;

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

/// <summary>
/// One logical container in an Xbox "wgs" (Connected Storage) folder. A thin view over the
/// generic container in <c>AbioticEditor.GamePass.Storage</c>: every property reads and writes the
/// underlying entry, so edits made through this type are what the store writes.
/// </summary>
public sealed class WgsContainer
{
    public WgsContainer() => Inner = new Storage.WgsContainer();

    // The required members are all backed by the inner entry, which is already populated.
#pragma warning disable CS8618
    [SetsRequiredMembers]
    internal WgsContainer(Storage.WgsContainer inner) => Inner = inner;
#pragma warning restore CS8618

    internal Storage.WgsContainer Inner { get; }

    public required string Name { get => Inner.Name; init => Inner.Name = value; }
    public required string Name2 { get => Inner.Name2; init => Inner.Name2 = value; }

    /// <summary>
    /// The container's ETag: a version token issued by the Xbox service, not by this machine.
    /// It is echoed back untouched on every local write - the service uses it to recognise which
    /// cloud version the local copy was based on. Generating a fresh one locally claims a version
    /// the service never issued, which is exactly how an upload stops matching.
    /// </summary>
    public required string Etag { get => Inner.Etag; set => Inner.Etag = value; }

    public byte ContainerNumber { get => Inner.ContainerNumber; set => Inner.ContainerNumber = value; }

    /// <summary>
    /// Where this container stands against its cloud copy. A save just rewritten is
    /// <see cref="WgsEntryState.Modified"/>; one that has never been uploaded is
    /// <see cref="WgsEntryState.Created"/>.
    /// </summary>
    public WgsEntryState State
    {
        get => (WgsEntryState)(uint)Inner.State;
        set => Inner.State = (Storage.WgsEntryState)(uint)value;
    }

    /// <summary>The raw state as read, so a value outside <see cref="WgsEntryState"/> can be
    /// reported and repaired rather than silently reinterpreted.</summary>
    public uint RawState { get => Inner.RawState; set => Inner.RawState = value; }

    /// <summary>True when the index carried a state this format does not define (anything above
    /// <see cref="WgsEntryState.Created"/>).</summary>
    public bool HasInvalidState => Inner.HasInvalidState;

    /// <summary>
    /// True when the state and the ETag contradict each other. Bit 2 of the state means "local
    /// only, never uploaded", which is exactly the case where there is no cloud version to name,
    /// so it must be set if and only if the ETag is empty. Two independently written parsers
    /// reject an entry that breaks this, so producing one risks a save other tools - and plausibly
    /// Xbox itself - treat as damaged.
    /// </summary>
    public bool StateContradictsEtag => Inner.StateContradictsEtag;

    public required Guid FolderGuid { get => Inner.FolderGuid; init => Inner.FolderGuid = value; }
    public long FileTime { get => Inner.FileTime; set => Inner.FileTime = value; }
    public long Reserved { get => Inner.Reserved; set => Inner.Reserved = value; }
    public long BlobSize { get => Inner.BlobSize; set => Inner.BlobSize = value; }

    public string FolderName => Inner.FolderName;
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
/// payload). See <c>docs/reference/game-pass-format.md</c> for the byte layout.
///
/// <para>This type is Abiotic Factor's adapter over the generic container layer in
/// <c>AbioticEditor.GamePass.Storage</c> (<see cref="Storage.WgsStore"/>): the container I/O, ETag
/// and state handling, write ordering and orphan discovery live there. What stays here is what is
/// specific to this title and this machine: the package family name, recognising an
/// <c>ABF_SAVE_VERSION</c> world bundle (to name orphaned worlds), the process/Connected Storage
/// write guard (<see cref="CheckWritable"/>), and the explicit write override.</para>
///
/// <para>Writing a blob follows the game's own scheme: a fresh GUID blob is written, a new
/// <c>container.&lt;N+1&gt;</c> points at it, and the index entry is updated (number bumped, state
/// moved to <see cref="WgsEntryState.Modified"/>, size, timestamp), then the superseded generation
/// is removed. The whole folder is backed up first (callers use <see cref="GamePassSaveSet"/>),
/// and that backup is the rollback.</para>
///
/// <para>This folder is one half of a conversation with the Xbox cloud, not a private file format.
/// State is set to what actually happened, the ETag is echoed rather than invented, and anything
/// whose meaning is not established is round-tripped verbatim.</para>
/// </summary>
public sealed class WgsContainerStore
{
    /// <summary>Abiotic Factor's Game Pass package family name + app id (public, identifies the title
    /// in a containers.index). Used when creating a container from scratch.</summary>
    public const string AbioticPackageFamilyName = "PlayStack.AbioticFactor_3wcqaesafpzfy!AppAbioticFactorShipping";

    /// <summary>The label used for the index-level conflict marker in the repair lists, which are otherwise
    /// container names.</summary>
    public const string CloudConflictLabel = Storage.WgsStore.CloudConflictLabel;

    /// <summary>
    /// The services this adapter hands the generic store: the editor log, the Abiotic world-bundle
    /// recogniser, and a permissive gate (this class runs its own, richer guard before every write).
    /// </summary>
    internal static Storage.WgsStoreOptions StorageOptions { get; } = new()
    {
        Log = new EditorLogAdapter(),
        BlobInspector = new AbfBlobInspector(),
        WriteGate = Storage.WgsWriteGates.AllowAll,
    };

    private readonly string _root;
    private readonly Storage.WgsStore _inner;
    private readonly Dictionary<Storage.WgsContainer, WgsContainer> _wrappers = new();
    private readonly List<WgsContainer> _containers = new();

    private WgsContainerStore(string root, Storage.WgsStore inner)
    {
        _root = root;
        _inner = inner;
        SyncWrappers();
    }

    /// <summary>The generic container store this adapter runs on, for callers that want its typed
    /// results (<c>TryWriteBlob</c>, <c>DetectExternalChange</c>, <c>Diagnose</c>).</summary>
    public Storage.WgsStore GenericStore => _inner;

    public IReadOnlyList<WgsContainer> Containers
    {
        get
        {
            SyncWrappers();
            return _containers;
        }
    }

    private void SyncWrappers()
    {
        if (_containers.Count == _inner.Containers.Count) return;
        foreach (var c in _inner.Containers)
        {
            if (_wrappers.ContainsKey(c)) continue;
            var wrapper = new WgsContainer(c);
            _wrappers[c] = wrapper;
            _containers.Add(wrapper);
        }
    }

    private WgsContainer? Wrap(Storage.WgsContainer? c)
    {
        if (c is null) return null;
        SyncWrappers();
        return _wrappers[c];
    }

    /// <summary>
    /// Logical containers whose manifest pointed at a blob that was missing from disk, so a sibling
    /// blob had to be used instead (see <see cref="ReadBlob"/>). A non-empty list is a reliable sign
    /// the save is mid-Xbox-sync: the index and the on-disk blobs disagree because cloud sync has not
    /// finished. Writing into a store in this state is what lets Xbox later discard the edited
    /// containers, so the host should warn before allowing edits.
    /// </summary>
    public IReadOnlyList<string> RecoveredContainers => _inner.RecoveredContainers;

    /// <summary>True when any container was read through the missing-blob fallback (save is mid-sync).</summary>
    public bool NeededBlobFallback => _inner.NeededBlobFallback;

    /// <summary>The package family name recorded in the index (identifies the owning title).</summary>
    public string PackageFamilyName => _inner.PackageFamilyName;

    /// <summary>The index-level FILETIME recorded in the header - the "last modified" recency token Xbox
    /// cloud sync compares to decide which copy (local vs cloud) is newer. The game advances it on every
    /// save; so does this editor.</summary>
    public long IndexFileTime => _inner.IndexFileTime;

    /// <summary>Index-level sync state (see <see cref="WgsSyncState"/>).</summary>
    public WgsSyncState SyncState => (WgsSyncState)(uint)_inner.SyncState;

    /// <summary>
    /// True when Xbox has a conflict for this save that it has not resolved. Writing into a store
    /// in this state is not a normal edit: the service already believes local and cloud disagree,
    /// so whatever is written here is one side of an argument that gets settled later, out of
    /// sight, and can be settled against you.
    /// </summary>
    public bool HasUnresolvedConflicts => _inner.HasUnresolvedConflicts;

    /// <summary>
    /// Containers whose recorded state is not a value the format defines. Only this editor is
    /// known to have produced them: it used to treat the state field as a write counter and
    /// increment it, walking containers through Deleted(3) and Created(4) and out the far end to
    /// 6, 7 and beyond. Reported so they can be put back to a real state.
    /// </summary>
    public IReadOnlyList<string> InvalidStateContainers => _inner.InvalidStateContainers;

    /// <summary>
    /// Containers a write must not be allowed to build on: their state is either outside the format
    /// entirely, or a deletion tombstone. Both describe a container the service is entitled to take
    /// away, so an edit written into one can vanish with it.
    /// </summary>
    public IReadOnlyList<string> UnsafeStateContainers => _inner.UnsafeStateContainers;

    /// <summary>
    /// Containers whose state and cloud version token contradict each other. Writing the container
    /// puts it right (see <see cref="WriteBlob"/>), so this is reported rather than refused.
    /// </summary>
    public IReadOnlyList<string> ContradictoryStateContainers => _inner.ContradictoryStateContainers;

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

    /// <summary>True when <paramref name="folder"/> directly contains a <c>containers.index</c>.</summary>
    public static bool IsContainerFolder(string folder) => Storage.WgsStore.IsContainerFolder(folder);

    /// <summary>
    /// Maps a folder the user picked to the actual wgs container folder (the one holding
    /// <c>containers.index</c>), tolerating the levels a Game Pass save tree invites a mis-click on:
    /// the container folder itself, its <c>wgs</c> / account parent (the picked folder has a child
    /// that is a container folder), or a GUID blob sub-folder (the picked folder's parent is the
    /// container folder). Returns null when nothing nearby is a container folder. Best-effort: an
    /// unreadable folder yields null rather than throwing.
    /// </summary>
    public static string? ResolveContainerFolder(string folder) => Storage.WgsStore.ResolveContainerFolder(folder);

    public static WgsContainerStore Open(string folder)
        => new(folder, Storage.WgsStore.Open(folder, StorageOptions));

    public WgsContainer? Find(string name) => Wrap(_inner.Find(name));

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
        => Storage.WgsStore.FindOrphanedContainers(folder, StorageOptions).Select(FromStorage).ToList();

    /// <summary>Orphaned container folders in this store (see <see cref="FindOrphanedContainers"/>).</summary>
    public IReadOnlyList<WgsOrphanedContainer> OrphanedContainers() => FindOrphanedContainers(_root);

    private static WgsOrphanedContainer FromStorage(Storage.WgsOrphanedContainer o)
        => new(o.FolderName, o.FolderPath, o.ContainerNumber, o.BlobId, o.BlobSize, o.LastWrittenUtc,
            o.Label, o.SuggestedContainerName);

    private static Storage.WgsOrphanedContainer ToStorage(WgsOrphanedContainer o)
        => new(o.FolderName, o.FolderPath, o.ContainerNumber, o.BlobId, o.BlobSize, o.LastWrittenUtc,
            o.WorldName, o.SuggestedContainerName);

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
        if (!Guid.TryParseExact(orphan.FolderName, "N", out _))
        {
            throw new InvalidOperationException($"'{orphan.FolderName}' is not a save data folder.");
        }

        return Wrap(_inner.ReRegisterOrphan(ToStorage(orphan), name))!;
    }

    /// <summary>Reads the blob bytes for a logical container (via its <c>container.N</c> manifest).</summary>
    public byte[] ReadBlob(WgsContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return _inner.ReadBlob(container.Inner);
    }

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
    public IReadOnlyList<string> ContainersNeedingRepair() => _inner.ContainersNeedingRepair();

    /// <summary>
    /// Fixes what <see cref="ContainersNeedingRepair"/> lists: clears the unresolved-conflict marker,
    /// puts container states back to a value the format defines, and repoints every manifest that was
    /// read through the missing-blob fallback at the blob actually present on disk (correcting the
    /// index size to match). It only repairs pointers and metadata, never the save data. Returns the
    /// container names repaired. Call with the game and Xbox app closed; the caller backs up first.
    /// </summary>
    public IReadOnlyList<string> RepairRecoveredManifests() => _inner.RepairRecoveredManifests();

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
    /// keeps exactly one <c>container.N</c> + one blob per folder and leftovers make the recovery
    /// path ambiguous. The whole folder is backed up before the first write
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
        _inner.WriteBlob(container.Inner, blob);
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
        EnsureWritable();
        _inner.AddOrReplaceContainer(containerName, blob);
        SyncWrappers();
    }

    /// <summary>
    /// Creates a brand-new single-container wgs folder at <paramref name="destFolder"/> holding one
    /// logical container (<paramref name="containerName"/>) whose blob is <paramref name="blob"/>.
    /// Used to convert a Steam world into a Game Pass save.
    ///
    /// <para>Refuses to run on a folder that already holds a <c>containers.index</c>. The index it
    /// writes describes exactly one container, so overwriting a real save store's index would
    /// orphan every other world and profile container in it - the folder would still hold the
    /// data, but nothing would reference it. Merge into an existing store with
    /// <see cref="AddOrReplaceContainer"/> instead.</para>
    /// </summary>
    public static void WriteNewContainer(string destFolder, string containerName, byte[] blob)
        => Storage.WgsStore.WriteNewContainer(destFolder, containerName, blob, AbioticPackageFamilyName, StorageOptions);

    private sealed class EditorLogAdapter : Storage.IWgsLog
    {
        public void Info(string message) => Diagnostics.EditorLog.Info("GamePass", message);
        public void Warn(string message) => Diagnostics.EditorLog.Warn("GamePass", message);
    }
}
