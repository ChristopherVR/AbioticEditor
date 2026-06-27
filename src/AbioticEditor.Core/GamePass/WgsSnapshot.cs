using System.Security.Cryptography;

namespace AbioticEditor.Core.GamePass;

/// <summary>One container's identity + content fingerprint at a moment in time.</summary>
public sealed record WgsContainerState(
    string Name, byte Number, uint Generation, long BlobSize, string? BlobSha256, string? Error);

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
        var store = WgsContainerStore.Open(folder);
        var states = new List<WgsContainerState>();
        foreach (var c in store.Containers)
        {
            string? sha = null, error = null;
            try
            {
                sha = Convert.ToHexString(SHA256.HashData(store.ReadBlob(c)));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            states.Add(new WgsContainerState(c.Name, c.ContainerNumber, c.Generation, c.BlobSize, sha, error));
        }
        return new WgsSnapshot(store.IndexFileTime, states);
    }

    /// <summary>
    /// Describes what changed between <paramref name="before"/> and <paramref name="after"/> in
    /// plain lines, flagging the outcomes that matter for "did my edit survive the sync?": a
    /// container DROPPED from the index, ROLLED BACK (generation went backwards), or whose content
    /// CHANGED. An empty result means the two snapshots are identical.
    /// </summary>
    public static IReadOnlyList<string> Compare(WgsSnapshot before, WgsSnapshot after)
    {
        var lines = new List<string>();
        var afterByName = after.Containers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var beforeByName = before.Containers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var b in before.Containers)
        {
            if (!afterByName.TryGetValue(b.Name, out var a))
            {
                lines.Add($"DROPPED   {b.Name} - removed from the index (Xbox sync discarded it)");
                continue;
            }
            if (a.Generation < b.Generation)
            {
                lines.Add($"ROLLED BACK {b.Name} - generation {b.Generation} -> {a.Generation} (reverted to an older copy)");
            }
            else if (!string.Equals(a.BlobSha256, b.BlobSha256, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"CHANGED   {b.Name} - content differs (gen {b.Generation} -> {a.Generation}, {b.BlobSize} -> {a.BlobSize} bytes)");
            }
        }
        foreach (var a in after.Containers)
        {
            if (!beforeByName.ContainsKey(a.Name))
            {
                lines.Add($"ADDED     {a.Name} - new container appeared");
            }
        }

        if (after.IndexFileTime != before.IndexFileTime)
        {
            var dir = after.IndexFileTime > before.IndexFileTime ? "advanced" : "WENT BACKWARDS";
            lines.Add($"index timestamp {dir}: {before.IndexFileTime} -> {after.IndexFileTime}");
        }
        return lines;
    }
}
