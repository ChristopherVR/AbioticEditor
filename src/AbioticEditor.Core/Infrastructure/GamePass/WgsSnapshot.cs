using Storage = AbioticEditor.GamePass.Storage;

namespace AbioticEditor.Core.GamePass;

/// <summary>One container's identity + content fingerprint at a moment in time.</summary>
/// <param name="Number">The <c>container.N</c> number, which is what advances on each write.</param>
/// <param name="State">How the container stands against its cloud copy. Recorded because watching
/// it go from Modified back to Synced without the content changing is Xbox having resolved a
/// conflict in the cloud's favour.</param>
public sealed record WgsContainerState(
    string Name, byte Number, WgsEntryState State, long BlobSize, string? BlobSha256, string? Error);

/// <summary>
/// A point-in-time fingerprint of a whole wgs folder: the index-level recency timestamp plus each
/// container's generation, size and a hash of its blob bytes. Comparing a snapshot taken before an
/// Xbox cloud sync with one taken after is the only way to actually observe, end-to-end on a real
/// machine, whether an edit survived the sync or was reverted/dropped - the Connected Storage sync
/// itself is driven by the game/Xbox app and cannot be invoked or faked from outside the title.
/// </summary>
public sealed record WgsSnapshot(long IndexFileTime, IReadOnlyList<WgsContainerState> Containers)
{
    /// <summary>Fingerprints every container in <paramref name="folder"/> (best-effort: a container
    /// whose blob can't be read is recorded with its Error rather than aborting the whole snapshot).</summary>
    public static WgsSnapshot Capture(string folder)
    {
        var snap = Storage.WgsSnapshot.Capture(Storage.WgsStore.Open(folder, WgsContainerStore.StorageOptions));
        return new WgsSnapshot(
            snap.IndexFileTime,
            snap.Containers.Select(c => new WgsContainerState(
                c.Name, c.Number, (WgsEntryState)(uint)c.State, c.BlobSize, c.BlobSha256, c.Error)).ToList());
    }

    /// <summary>
    /// Describes what changed between <paramref name="before"/> and <paramref name="after"/> in
    /// plain lines, flagging the outcomes that matter for "did my edit survive the sync?": a
    /// container DROPPED from the index, ROLLED BACK (generation went backwards), or whose content
    /// CHANGED. An empty result means the two snapshots are identical.
    /// </summary>
    public static IReadOnlyList<string> Compare(WgsSnapshot before, WgsSnapshot after)
        => Storage.WgsSnapshot.Compare(ToStorage(before), ToStorage(after));

    private static Storage.WgsSnapshot ToStorage(WgsSnapshot s)
        => new(s.IndexFileTime, s.Containers.Select(c => new Storage.WgsContainerState(
            c.Name, c.Number, (Storage.WgsEntryState)(uint)c.State, c.BlobSize, c.BlobSha256, c.Error)).ToList());
}
