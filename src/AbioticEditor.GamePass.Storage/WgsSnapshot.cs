using System.Security.Cryptography;

namespace AbioticEditor.GamePass.Storage;

/// <summary>One container's identity and content fingerprint at a moment in time.</summary>
/// <param name="Number">The <c>container.N</c> number, which advances on each write.</param>
/// <param name="State">How the container stands against its cloud copy. Watching it go from
/// Modified back to Synced without the content changing is Xbox resolving a conflict in the cloud's favour.</param>
public sealed record WgsContainerState(
    string Name, byte Number, WgsEntryState State, long BlobSize, string? BlobSha256, string? Error);

/// <summary>
/// A point-in-time fingerprint of a whole wgs folder. Comparing a snapshot taken before an Xbox
/// cloud sync with one taken after is the only way to observe, end-to-end on a real machine,
/// whether an edit survived the sync; the sync itself is driven by the game or Xbox app and cannot
/// be invoked from outside.
/// </summary>
public sealed record WgsSnapshot(long IndexFileTime, IReadOnlyList<WgsContainerState> Containers)
{
    /// <summary>Fingerprints every container in an open store (best effort: an unreadable blob is
    /// recorded with its Error rather than aborting the snapshot).</summary>
    public static WgsSnapshot Capture(WgsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
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
            states.Add(new WgsContainerState(c.Name, c.ContainerNumber, c.State, c.BlobSize, sha, error));
        }
        return new WgsSnapshot(store.IndexFileTime, states);
    }

    /// <summary>
    /// Describes what changed between two snapshots, flagging DROPPED, ROLLED BACK and CHANGED
    /// containers, containers Xbox RESOLVED, and ADDED ones. An empty result means identical.
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
            // The number is a byte and wraps at 255, so a backwards step across that boundary reads
            // as a jump forward; content is compared regardless, which is what catches it.
            if (a.Number < b.Number)
            {
                lines.Add($"ROLLED BACK {b.Name} - container {b.Number} -> {a.Number} (reverted to an older copy)");
            }
            else if (!string.Equals(a.BlobSha256, b.BlobSha256, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"CHANGED   {b.Name} - content differs (container {b.Number} -> {a.Number}, {b.BlobSize} -> {a.BlobSize} bytes)");
            }
            else if (b.State == WgsEntryState.Modified && a.State == WgsEntryState.Synced)
            {
                lines.Add($"RESOLVED  {b.Name} - Xbox marked it synced (content unchanged)");
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
