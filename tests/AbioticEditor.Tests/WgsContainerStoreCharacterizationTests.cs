using System.Security.Cryptography;
using System.Text;
using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// Characterization tests for the wgs container layer (docs/reference/game-pass-extraction-inventory.md).
/// They pin what the store does TODAY, over the sanitized Game Pass fixture and over synthetic
/// temp-dir stores, so the container layer can move into its own package without changing
/// behavior. They deliberately go through the public Core API only: they must keep passing,
/// unmodified, before and after the extraction.
/// </summary>
public class WgsContainerStoreCharacterizationTests
{
    private const string IndexName = "containers.index";

    // ---- the real fixture -------------------------------------------------------------------

    [SkippableFact]
    public void The_fixture_index_reads_as_the_documented_layout()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "no Game Pass fixture in this checkout");
        var store = WgsContainerStore.Open(Fixtures.GamePassWgsDir!);

        Assert.StartsWith("Synthetic.AbioticTest_", store.PackageFamilyName, StringComparison.Ordinal);
        Assert.Equal(WgsSyncState.FullyUploaded | WgsSyncState.FullyDownloaded, store.SyncState);
        Assert.False(store.HasUnresolvedConflicts);
        Assert.Empty(store.InvalidStateContainers);
        Assert.Empty(store.RecoveredContainers);

        var c = Assert.Single(store.Containers);
        Assert.Equal("TestWorld-WC", c.Name);
        Assert.Equal("TestWorld-WC", c.Name2);
        Assert.Equal("\"0x1\"", c.Etag);
        Assert.Equal((byte)1, c.ContainerNumber);
        Assert.Equal(WgsEntryState.Synced, c.State);
        Assert.Equal("B4EB70880D954F7EA9F5B594BA605699", c.FolderName);
        Assert.Equal(24034, c.BlobSize);
        Assert.Same(c, store.Find("testworld-wc"));
    }

    [SkippableFact]
    public void The_fixture_blob_is_read_through_its_manifest_and_matches_the_recorded_size()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "no Game Pass fixture in this checkout");
        var store = WgsContainerStore.Open(Fixtures.GamePassWgsDir!);

        var blob = store.ReadBlob(store.Containers[0]);

        Assert.Equal(store.Containers[0].BlobSize, blob.Length);
        Assert.False(store.NeededBlobFallback);
    }

    [SkippableFact]
    public void Reading_never_changes_a_single_byte_of_the_store()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "no Game Pass fixture in this checkout");
        using var scratch = new Scratch();
        CopyTree(Fixtures.GamePassWgsDir!, scratch.Path);
        var before = HashTree(scratch.Path);

        var store = WgsContainerStore.Open(scratch.Path);
        _ = store.ReadBlob(store.Containers[0]);
        _ = store.ContainersNeedingRepair();
        _ = store.OrphanedContainers();
        _ = store.CheckWritable();
        _ = WgsContainerStore.FindOrphanedContainers(scratch.Path);
        _ = WgsSnapshot.Capture(scratch.Path);

        Assert.Equal(before, HashTree(scratch.Path));
    }

    [SkippableFact]
    public void A_snapshot_of_an_untouched_fixture_compares_equal_to_itself()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "no Game Pass fixture in this checkout");

        var a = WgsSnapshot.Capture(Fixtures.GamePassWgsDir!);
        var b = WgsSnapshot.Capture(Fixtures.GamePassWgsDir!);

        Assert.Empty(WgsSnapshot.Compare(a, b));
        var state = Assert.Single(a.Containers);
        Assert.Equal("TestWorld-WC", state.Name);
        Assert.Equal(64, state.BlobSha256!.Length);
        Assert.Null(state.Error);
    }

    [SkippableFact]
    public void Writing_a_fixture_copy_touches_only_what_the_game_itself_would_touch()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "no Game Pass fixture in this checkout");
        using var scratch = new Scratch();
        CopyTree(Fixtures.GamePassWgsDir!, scratch.Path);
        var beforeIndex = File.ReadAllBytes(Path.Combine(scratch.Path, IndexName));

        var store = WgsContainerStore.Open(scratch.Path);
        var replacement = Payload(3000, seed: 5);
        store.WriteBlob(store.Containers[0], replacement);

        var afterIndex = File.ReadAllBytes(Path.Combine(scratch.Path, IndexName));
        var entryStart = FindEntryStart(beforeIndex);

        // Header: identical except the index FILETIME (8 bytes) and the sync flags (4 bytes).
        var header = entryStart;
        var diffs = Enumerable.Range(0, header).Where(i => beforeIndex[i] != afterIndex[i]).ToList();
        var fileTimeAt = 16 + (int)BitConverter.ToUInt32(beforeIndex, 12) * 2;
        var flagsAt = fileTimeAt + 8;
        Assert.All(diffs, i => Assert.InRange(i, fileTimeAt, flagsAt + 3));
        Assert.Contains(diffs, i => i < flagsAt); // the recency token advanced
        Assert.Equal(1u, BitConverter.ToUInt32(beforeIndex, flagsAt) & 1u);
        Assert.Equal(0u, BitConverter.ToUInt32(afterIndex, flagsAt) & 1u); // FullyUploaded cleared

        // The ETag is echoed untouched; the number advances; the state is what actually happened.
        var reopened = WgsContainerStore.Open(scratch.Path);
        var c = Assert.Single(reopened.Containers);
        Assert.Equal("\"0x1\"", c.Etag);
        Assert.Equal((byte)2, c.ContainerNumber);
        Assert.Equal(WgsEntryState.Modified, c.State);
        Assert.Equal(replacement.Length, c.BlobSize);
        Assert.Equal(replacement, reopened.ReadBlob(c));
    }

    // ---- synthetic stores -------------------------------------------------------------------

    [SkippableFact]
    public void A_new_container_is_created_never_uploaded_and_has_no_etag()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));

        var store = WgsContainerStore.Open(scratch.Path);
        var c = Assert.Single(store.Containers);

        Assert.Equal(WgsEntryState.Created, c.State);
        Assert.Equal(string.Empty, c.Etag);
        Assert.Equal((byte)1, c.ContainerNumber);
        Assert.Equal(WgsSyncState.None, store.SyncState);
        Assert.Equal(WgsContainerStore.AbioticPackageFamilyName, store.PackageFamilyName);
        Assert.Equal(0, c.FileTime % 10_000); // the game stamps whole milliseconds
    }

    [SkippableFact]
    public void A_created_container_stays_created_after_a_write_and_an_etagged_one_becomes_modified()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        store.WriteBlob(store.Containers[0], Payload(120, 2));
        Assert.Equal(WgsEntryState.Created, WgsContainerStore.Open(scratch.Path).Containers[0].State);

        store.Containers[0].Etag = "\"0x8DEBCCC41BE9635\"";
        store.Containers[0].State = WgsEntryState.Synced;
        store.Containers[0].RawState = (uint)WgsEntryState.Synced;
        store.WriteBlob(store.Containers[0], Payload(130, 3));

        var after = WgsContainerStore.Open(scratch.Path).Containers[0];
        Assert.Equal(WgsEntryState.Modified, after.State);
        Assert.Equal("\"0x8DEBCCC41BE9635\"", after.Etag);
    }

    [SkippableFact]
    public void A_write_keeps_exactly_one_manifest_and_one_blob_and_leaves_no_temp_files()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        for (var i = 0; i < 3; i++) store.WriteBlob(store.Containers[0], Payload(200 + i, 10 + i));

        var folder = Path.Combine(scratch.Path, store.Containers[0].FolderName);
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains("container.4", files);
        Assert.DoesNotContain(Directory.EnumerateFiles(scratch.Path, "*.tmp", SearchOption.AllDirectories), _ => true);
    }

    [SkippableFact]
    public void The_container_number_is_a_byte_and_wraps_past_255()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(64, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        var last = Payload(80, 99);
        for (var i = 0; i < 255; i++) store.WriteBlob(store.Containers[0], i == 254 ? last : Payload(64, i));

        var c = WgsContainerStore.Open(scratch.Path).Containers[0];
        Assert.Equal((byte)0, c.ContainerNumber); // 1 + 255 wraps to 0
        Assert.True(File.Exists(Path.Combine(scratch.Path, c.FolderName, "container.0")));
        Assert.Equal(last, WgsContainerStore.Open(scratch.Path).ReadBlob(c));
    }

    [SkippableFact]
    public void The_index_timestamp_advances_strictly_on_every_write()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(64, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        var last = store.IndexFileTime;
        for (var i = 0; i < 5; i++)
        {
            store.WriteBlob(store.Containers[0], Payload(64, i));
            Assert.True(store.IndexFileTime > last);
            last = store.IndexFileTime;
        }
        Assert.Equal(last, WgsContainerStore.Open(scratch.Path).IndexFileTime);
    }

    [SkippableFact]
    public void Adding_a_container_keeps_every_other_container_byte_identical()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "A-WC", Payload(100, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        store.AddOrReplaceContainer("B-WC", Payload(200, 2));

        var a = store.Find("A-WC")!;
        var beforeA = HashTree(Path.Combine(scratch.Path, a.FolderName));

        store.AddOrReplaceContainer("C-WC", Payload(300, 3));      // add
        store.AddOrReplaceContainer("B-WC", Payload(250, 4));      // replace

        Assert.Equal(beforeA, HashTree(Path.Combine(scratch.Path, a.FolderName)));
        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(["A-WC", "B-WC", "C-WC"], reopened.Containers.Select(c => c.Name).ToArray());
        Assert.Equal(3u, BitConverter.ToUInt32(File.ReadAllBytes(Path.Combine(scratch.Path, IndexName)), 4));
        Assert.Equal(Payload(300, 3), reopened.ReadBlob(reopened.Find("C-WC")!));
        Assert.Equal((byte)2, reopened.Find("B-WC")!.ContainerNumber);
    }

    [SkippableFact]
    public void Unicode_container_names_and_etags_round_trip_exactly()
    {
        using var scratch = new Scratch();
        const string name = "Wörld 日本語 \U0001F30D-WC";
        WgsContainerStore.WriteNewContainer(scratch.Path, name, Payload(100, 1));

        var store = WgsContainerStore.Open(scratch.Path);
        store.Containers[0].Etag = "\"0xé\"";
        store.Containers[0].State = WgsEntryState.Synced;
        store.Containers[0].RawState = (uint)WgsEntryState.Synced;
        store.AddOrReplaceContainer("Plain-WC", Payload(10, 2));
        store.WriteBlob(store.Containers[0], Payload(110, 3));

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(name, reopened.Containers[0].Name);
        Assert.Equal(name, reopened.Containers[0].Name2);
        Assert.Equal("\"0xé\"", reopened.Containers[0].Etag);
        Assert.Same(reopened.Containers[0], reopened.Find(name.ToUpperInvariant()));
    }

    [SkippableFact]
    public void Writing_a_new_container_list_over_an_existing_store_is_refused()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var before = HashTree(scratch.Path);

        Assert.Throws<InvalidOperationException>(
            () => WgsContainerStore.WriteNewContainer(scratch.Path, "Other-WC", Payload(10, 2)));

        Assert.Equal(before, HashTree(scratch.Path));
    }

    // ---- missing / damaged / orphaned -------------------------------------------------------

    [SkippableFact]
    public void A_missing_manifest_or_folder_fails_the_read_with_an_io_error()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        var folder = Path.Combine(scratch.Path, store.Containers[0].FolderName);

        File.Delete(Path.Combine(folder, "container.1"));
        Assert.ThrowsAny<IOException>(() => store.ReadBlob(store.Containers[0]));

        Directory.Delete(folder, recursive: true);
        Assert.ThrowsAny<IOException>(() => store.ReadBlob(store.Containers[0]));
    }

    [SkippableFact]
    public void A_truncated_index_fails_to_open()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var path = Path.Combine(scratch.Path, IndexName);
        var bytes = File.ReadAllBytes(path);

        File.WriteAllBytes(path, bytes[..40]);

        Assert.ThrowsAny<Exception>(() => WgsContainerStore.Open(scratch.Path));
    }

    [SkippableFact]
    public void A_folder_the_index_forgot_is_listed_as_an_orphan_and_can_be_put_back_without_copying()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Live-WC", Payload(100, 1));
        var store = WgsContainerStore.Open(scratch.Path);
        var live = store.Containers[0];

        // A second GUID folder holding a manifest + blob that no index entry names.
        var orphanFolder = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var orphanBlob = Payload(777, 8);
        var blobGuid = Guid.NewGuid();
        var dir = Path.Combine(scratch.Path, orphanFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, blobGuid.ToString("N").ToUpperInvariant()), orphanBlob);
        File.WriteAllBytes(Path.Combine(dir, "container.7"), Manifest(blobGuid));
        var indexBefore = File.ReadAllBytes(Path.Combine(scratch.Path, IndexName));

        var found = Assert.Single(store.OrphanedContainers());
        Assert.Equal(orphanFolder, found.FolderName);
        Assert.Equal((byte)7, found.ContainerNumber);
        Assert.Equal(777, found.BlobSize);
        Assert.Equal(blobGuid, found.BlobId);
        Assert.Null(found.WorldName);              // not an ABF bundle, so nothing to name it
        Assert.Null(found.SuggestedContainerName);
        Assert.True(WgsContainerStore.HasOrphanedWorldFolders(scratch.Path));
        // Finding is a read: the index is untouched.
        Assert.Equal(indexBefore, File.ReadAllBytes(Path.Combine(scratch.Path, IndexName)));

        Assert.Throws<InvalidOperationException>(() => store.ReRegisterOrphan(found)); // no name
        Assert.Throws<InvalidOperationException>(() => store.ReRegisterOrphan(found, "live-wc")); // taken

        var put = store.ReRegisterOrphan(found, "Back-WC");
        Assert.Equal(WgsEntryState.Created, put.State);
        Assert.Equal(string.Empty, put.Etag);
        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(orphanBlob, reopened.ReadBlob(reopened.Find("Back-WC")!));
        Assert.Equal(live.FolderName, reopened.Find("Live-WC")!.FolderName);
        Assert.Empty(reopened.OrphanedContainers());
    }

    [SkippableTheory]
    [InlineData("MyWorld", false)]
    [InlineData("Wörld日本", true)]
    public void An_orphaned_world_bundle_is_named_from_its_table_of_contents(string world, bool wide)
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Live-WC", Payload(100, 1));
        var dir = Path.Combine(scratch.Path, Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(dir, blobGuid.ToString("N").ToUpperInvariant()), AbfHead(world, wide));
        File.WriteAllBytes(Path.Combine(dir, "container.3"), Manifest(blobGuid));

        var found = Assert.Single(WgsContainerStore.FindOrphanedContainers(scratch.Path));

        Assert.Equal(world, found.WorldName);
        Assert.Equal(world + "-WC", found.SuggestedContainerName);
    }

    [SkippableFact]
    public void An_orphan_named_like_a_live_world_gets_no_suggested_name()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "MyWorld-WC", Payload(100, 1));
        var dir = Path.Combine(scratch.Path, Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(dir);
        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(dir, blobGuid.ToString("N").ToUpperInvariant()), AbfHead("MyWorld", false));
        File.WriteAllBytes(Path.Combine(dir, "container.3"), Manifest(blobGuid));

        var found = Assert.Single(WgsContainerStore.FindOrphanedContainers(scratch.Path));

        Assert.Equal("MyWorld", found.WorldName);
        Assert.Null(found.SuggestedContainerName);
    }

    [SkippableFact]
    public void The_container_folder_is_resolved_from_the_folder_a_user_might_pick()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var blobFolder = Directory.GetDirectories(scratch.Path).Single();

        Assert.Equal(scratch.Path, WgsContainerStore.ResolveContainerFolder(scratch.Path));
        Assert.Equal(scratch.Path, WgsContainerStore.ResolveContainerFolder(Path.GetDirectoryName(scratch.Path)!));
        Assert.Equal(scratch.Path, WgsContainerStore.ResolveContainerFolder(blobFolder));
        // Any path directly beneath a container folder resolves to it, existing or not.
        Assert.Equal(scratch.Path, WgsContainerStore.ResolveContainerFolder(Path.Combine(scratch.Path, "nope")));
        Assert.Null(WgsContainerStore.ResolveContainerFolder(Path.Combine(Path.GetDirectoryName(scratch.Path)!, "nope")));
        Assert.Null(WgsContainerStore.ResolveContainerFolder("  "));
        Assert.True(WgsContainerStore.IsContainerFolder(scratch.Path));
        Assert.False(WgsContainerStore.IsContainerFolder(blobFolder));
    }

    [SkippableFact]
    public void The_abiotic_check_reads_only_the_package_family_name()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        Assert.True(WgsContainerStore.IsAbioticContainerFolder(scratch.Path));
        Assert.False(WgsContainerStore.IsAbioticContainerFolder(Path.Combine(scratch.Path, "missing")));
    }

    [SkippableFact]
    public void A_snapshot_flags_a_written_container_as_changed()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(100, 1));
        var before = WgsSnapshot.Capture(scratch.Path);
        var store = WgsContainerStore.Open(scratch.Path);
        store.WriteBlob(store.Containers[0], Payload(101, 2));

        var diff = WgsSnapshot.Compare(before, WgsSnapshot.Capture(scratch.Path));

        Assert.Contains(diff, l => l.StartsWith("CHANGED", StringComparison.Ordinal));
        Assert.Contains(diff, l => l.Contains("index timestamp advanced", StringComparison.Ordinal));
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static int FindEntryStart(byte[] index)
    {
        // Fixed header: 12 + wstring family name + 8 FILETIME + 4 flags + wstring root guid + 8 reserved.
        var pos = 12;
        pos += 4 + (int)BitConverter.ToUInt32(index, pos) * 2;
        pos += 12;
        pos += 4 + (int)BitConverter.ToUInt32(index, pos) * 2;
        return pos + 8;
    }

    private static byte[] Manifest(Guid blob)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(4u);
        w.Write(1u);
        var name = new byte[128];
        Encoding.Unicode.GetBytes("Data").CopyTo(name, 0);
        w.Write(name);
        w.Write(blob.ToByteArray());
        w.Write(blob.ToByteArray());
        return ms.ToArray();
    }

    private static byte[] AbfHead(string world, bool wide)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteFString(w, "ABF_SAVE_VERSION", false);
        w.Write(new byte[16]);
        WriteFString(w, $"Profile/Worlds/{world}/WorldSave_MetaData.sav", wide);
        w.Write(new byte[64]);
        return ms.ToArray();
    }

    private static void WriteFString(BinaryWriter w, string s, bool wide)
    {
        if (!wide)
        {
            w.Write(s.Length + 1);
            w.Write(Encoding.ASCII.GetBytes(s));
            w.Write((byte)0);
        }
        else
        {
            w.Write(-(s.Length + 1));
            w.Write(Encoding.Unicode.GetBytes(s));
            w.Write((ushort)0);
        }
    }

    private static byte[] Payload(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static Dictionary<string, string> HashTree(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f).Replace('\\', '/'),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))),
                StringComparer.Ordinal);

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(source)) CopyTree(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("abiotic-wgs-char-");
        public string Path => System.IO.Path.Combine(_dir.FullName, "wgs");
        public void Dispose()
        {
            try { _dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
