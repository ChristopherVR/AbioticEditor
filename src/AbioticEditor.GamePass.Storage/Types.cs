namespace AbioticEditor.GamePass.Storage;

/// <summary>How Xbox Connected Storage tracks one container against its cloud copy.
/// Mapping follows libNOM.io (see docs/reference/game-pass-format.md).</summary>
public enum WgsEntryState : uint
{
    UnknownZero = 0,
    /// <summary>Local and cloud agree.</summary>
    Synced = 1,
    /// <summary>Changed locally since the last sync; keeps its ETag.</summary>
    Modified = 2,
    /// <summary>A tombstone kept so the deletion can reach the cloud.</summary>
    Deleted = 3,
    UnknownFour = 4,
    /// <summary>Made locally and never uploaded; the ETag is empty.</summary>
    Created = 5,
}

/// <summary>Index-level sync flags.</summary>
[Flags]
public enum WgsSyncState : uint
{
    None = 0,
    FullyUploaded = 1 << 0,
    FullyDownloaded = 1 << 1,
    HasUnresolvedConflicts = 1 << 4,
}

/// <summary>One logical container in a wgs folder.</summary>
public sealed class WgsContainer
{
    public string Name { get; set; } = string.Empty;
    public string Name2 { get; set; } = string.Empty;

    /// <summary>Service-issued version token. Echoed back untouched on every local write.</summary>
    public string Etag { get; set; } = string.Empty;

    public byte ContainerNumber { get; set; }
    public WgsEntryState State { get; set; } = WgsEntryState.Synced;

    /// <summary>The state as read, so an undefined value can be reported and repaired.</summary>
    public uint RawState { get; set; } = (uint)WgsEntryState.Synced;

    public bool HasInvalidState => RawState > (uint)WgsEntryState.Created;

    /// <summary>Bit 2 of the state means "never uploaded", so it must be set iff the ETag is empty.</summary>
    public bool StateContradictsEtag => ((RawState & 4) != 0) != string.IsNullOrEmpty(Etag);

    public Guid FolderGuid { get; set; }
    public long FileTime { get; set; }
    public long Reserved { get; set; }
    public long BlobSize { get; set; }

    public string FolderName => FolderGuid.ToString("N").ToUpperInvariant();
}

/// <summary>Save data in a wgs folder that no index entry points at.</summary>
public sealed record WgsOrphanedContainer(
    string FolderName,
    string FolderPath,
    byte ContainerNumber,
    Guid BlobId,
    long BlobSize,
    DateTime LastWrittenUtc,
    string? Label,
    string? SuggestedContainerName);

/// <summary>Both blob ids a manifest records, and how many blob entries it declares.</summary>
public sealed record WgsManifestInfo(Guid Current, Guid Previous, uint BlobCount);

/// <summary>Outcome of opening a store without throwing.</summary>
public enum WgsOpenStatus
{
    Opened,
    NotAContainerFolder,
    /// <summary>containers.index exists but could not be parsed as the known layout.</summary>
    UnsupportedLayout,
    /// <summary>Another process holds the index open exclusively.</summary>
    LockConflict,
    Failed,
}

public sealed record WgsOpenResult(WgsOpenStatus Status, WgsStore? Store, string? Message)
{
    public bool Succeeded => Status == WgsOpenStatus.Opened;
}

/// <summary>Outcome of a read or write that did not throw.</summary>
public enum WgsOperationStatus
{
    Ok,
    /// <summary>The write gate refused (unresolved conflict, unsafe state, platform concern).</summary>
    Refused,
    /// <summary>A file was held by another process (sharing violation or access denied).</summary>
    LockConflict,
    /// <summary>The store on disk no longer matches what was inspected; re-open and re-evaluate.</summary>
    ConcurrentChange,
    /// <summary>A manifest or index shape this package does not model (for instance several blobs).</summary>
    UnsupportedLayout,
    /// <summary>The blob a manifest names is missing and no safe alternative exists.</summary>
    MissingBlob,
    /// <summary>Two different blobs are on disk: a sync is in flight and nothing says which should win.</summary>
    SyncInFlight,
    Failed,
}

public sealed record WgsReadResult(WgsOperationStatus Status, byte[]? Blob, string? Message, bool UsedFallback)
{
    public bool Succeeded => Status == WgsOperationStatus.Ok;
}

public sealed record WgsCommitResult(WgsOperationStatus Status, WgsContainer? Container, string? Message,
    WgsWriteAssessment? Assessment = null)
{
    public bool Succeeded => Status == WgsOperationStatus.Ok;
}

/// <summary>What changed on disk since a store was opened (or last committed by this instance).</summary>
public sealed record WgsChangeReport(bool Changed, IReadOnlyList<string> Reasons)
{
    public static WgsChangeReport Unchanged { get; } = new(false, Array.Empty<string>());
}

/// <summary>A preview of what a blob write would do. Nothing is touched to produce it.</summary>
public sealed record WgsWritePlan(
    string ContainerName,
    byte CurrentNumber,
    byte NewNumber,
    WgsEntryState NewState,
    long NewBlobSize,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> FilesToRemove,
    WgsWriteAssessment Assessment);

/// <summary>Everything worth knowing about a store's health, gathered without changing it.</summary>
public sealed record WgsDiagnosis(
    uint IndexVersion,
    bool IsKnownIndexVersion,
    string PackageFamilyName,
    WgsSyncState SyncState,
    int ContainerCount,
    IReadOnlyList<string> InvalidStateContainers,
    IReadOnlyList<string> UnsafeStateContainers,
    IReadOnlyList<string> ContainersNeedingRepair,
    IReadOnlyList<string> MultiBlobContainers,
    IReadOnlyList<WgsOrphanedContainer> Orphans,
    WgsWriteAssessment WriteAssessment);

/// <summary>Thrown by throwing write paths when the write gate refuses.</summary>
public sealed class WgsWriteRefusedException : InvalidOperationException
{
    public WgsWriteRefusedException(WgsWriteAssessment assessment) : base(assessment.BlockingMessage())
        => Assessment = assessment;

    public WgsWriteRefusedException() { }
    public WgsWriteRefusedException(string message) : base(message) { }
    public WgsWriteRefusedException(string message, Exception innerException) : base(message, innerException) { }

    public WgsWriteAssessment Assessment { get; } = WgsWriteAssessment.Clear;
}
