using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// Models the one thing that determines whether a Game Pass edit survives Xbox cloud sync: the
/// per-container-set recency comparison. Microsoft's Connected Storage docs describe sync as a
/// local-vs-cloud comparison whose conflict resolution is driven by the saved "last modified" state,
/// and the <c>containers.index</c> carries a header FILETIME that the game rewrites on every save.
/// So a faithful editor write must look <em>strictly newer</em> than what was there, the same as a
/// game save, or Xbox treats the local copy as stale and rolls it back to the cloud version.
///
/// These tests can't reach the real Xbox service, so they assert the local invariants that the
/// service's decision is built on (the index timestamp advances, generations bump, the store stays
/// self-consistent) and run the recency rule itself over a simulated cloud/local pair.
/// </summary>
public class GamePassSyncFidelityTests
{
    /// <summary>Reads the index-level FILETIME (the field cloud sync uses to rank copies by recency).</summary>
    private static long IndexFileTime(string root)
    {
        var d = File.ReadAllBytes(Path.Combine(root, "containers.index"));
        var pos = 0;
        uint U32() { var v = BitConverter.ToUInt32(d, pos); pos += 4; return v; }
        U32();                       // version
        U32();                       // container count
        U32();                       // reserved
        var pfnLen = (int)U32();     // package-family-name length (UTF-16 chars)
        pos += pfnLen * 2;           // skip the name
        return BitConverter.ToInt64(d, pos);
    }

    [Fact]
    public void EditorWrite_advances_index_timestamp_generation_and_stays_consistent()
    {
        var dir = Directory.CreateTempSubdirectory("gp-sync");
        try
        {
            // The starting state stands in for the copy already known to the cloud.
            WgsContainerStore.WriteNewContainer(dir.FullName, "W-WC", new byte[] { 1, 2, 3, 4 });
            var cloudStamp = IndexFileTime(dir.FullName);

            var store = WgsContainerStore.Open(dir.FullName);
            var c = store.Find("W-WC")!;
            var (oldGen, oldNum) = (c.Generation, c.ContainerNumber);

            // Simulate the editor saving an edit into the container.
            store.WriteBlob(c, new byte[] { 9, 9, 9, 9, 9, 9 });
            var localStamp = IndexFileTime(dir.FullName);

            // The crux: the edited index must read strictly newer, so Xbox's recency-based conflict
            // resolution keeps the local (edited) copy instead of pulling the cloud copy back.
            Assert.True(localStamp > cloudStamp, $"index timestamp must advance: {cloudStamp} -> {localStamp}");

            // Per-container bookkeeping advances like a real save, and the store re-reads cleanly -
            // the manifest points at the blob actually on disk (no mid-sync inconsistency we created).
            var reopened = WgsContainerStore.Open(dir.FullName);
            var c2 = reopened.Find("W-WC")!;
            Assert.Equal(unchecked((uint)(oldGen + 1)), c2.Generation);
            Assert.Equal(unchecked((byte)(oldNum + 1)), c2.ContainerNumber);
            Assert.False(reopened.NeededBlobFallback);
            Assert.Equal(new byte[] { 9, 9, 9, 9, 9, 9 }, reopened.ReadBlob(c2));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Simulated_sync_conflict_resolves_to_the_edited_local_copy()
    {
        var dir = Directory.CreateTempSubdirectory("gp-sync2");
        try
        {
            WgsContainerStore.WriteNewContainer(dir.FullName, "W-WC", new byte[] { 1 });
            var cloudStamp = IndexFileTime(dir.FullName);

            var store = WgsContainerStore.Open(dir.FullName);
            store.WriteBlob(store.Find("W-WC")!, new byte[] { 2, 2 });
            var localStamp = IndexFileTime(dir.FullName);

            // Run the recency rule the platform uses: the newer container set wins the conflict.
            var winner = localStamp > cloudStamp ? "local" : "cloud";
            Assert.Equal("local", winner);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Snapshot_compare_detects_an_edit_as_a_content_change()
    {
        var dir = Directory.CreateTempSubdirectory("gp-snap");
        try
        {
            WgsContainerStore.WriteNewContainer(dir.FullName, "W-WC", new byte[] { 1, 2, 3 });
            var before = WgsSnapshot.Capture(dir.FullName);

            var store = WgsContainerStore.Open(dir.FullName);
            store.WriteBlob(store.Find("W-WC")!, new byte[] { 7, 7, 7, 7 });
            var after = WgsSnapshot.Capture(dir.FullName);

            var diff = WgsSnapshot.Compare(before, after);
            Assert.Contains(diff, l => l.StartsWith("CHANGED", System.StringComparison.Ordinal) && l.Contains("W-WC"));
            Assert.Contains(diff, l => l.Contains("index timestamp advanced"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Snapshot_compare_flags_a_dropped_and_a_rolled_back_container()
    {
        // The two outcomes a real Xbox sync inflicts on edits: dropping a container from the index,
        // and rolling one back to an older generation. Compare must call both out by name.
        var before = new WgsSnapshot(100, new[]
        {
            new WgsContainerState("World-WC", 5, 5, 100, "AAAA", null),
            new WgsContainerState("Settings", 3, 2, 50, "BBBB", null),
        });
        var after = new WgsSnapshot(90, new[]
        {
            // World-WC is gone; Settings reverted from gen 2 to gen 1.
            new WgsContainerState("Settings", 2, 1, 50, "CCCC", null),
        });

        var diff = WgsSnapshot.Compare(before, after);
        Assert.Contains(diff, l => l.StartsWith("DROPPED", System.StringComparison.Ordinal) && l.Contains("World-WC"));
        Assert.Contains(diff, l => l.StartsWith("ROLLED BACK", System.StringComparison.Ordinal) && l.Contains("Settings"));
        Assert.Contains(diff, l => l.Contains("WENT BACKWARDS"));
    }

    [Fact]
    public void Successive_edits_keep_advancing_the_timestamp_even_back_to_back()
    {
        var dir = Directory.CreateTempSubdirectory("gp-sync3");
        try
        {
            WgsContainerStore.WriteNewContainer(dir.FullName, "W-WC", new byte[] { 0 });
            var store = WgsContainerStore.Open(dir.FullName);
            var c = store.Find("W-WC")!;

            // Two quick writes in a row (likely within one clock tick) must still produce strictly
            // increasing index timestamps - monotonicity is what makes the comparison reliable.
            long t0 = IndexFileTime(dir.FullName);
            store.WriteBlob(c, new byte[] { 1 });
            long t1 = IndexFileTime(dir.FullName);
            store.WriteBlob(c, new byte[] { 2 });
            long t2 = IndexFileTime(dir.FullName);

            Assert.True(t1 > t0, $"{t0} -> {t1}");
            Assert.True(t2 > t1, $"{t1} -> {t2}");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
