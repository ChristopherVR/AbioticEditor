using System.Text;
using AbioticEditor.GamePass.Storage;
using CoreGp = AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// The generic wgs container package (<c>AbioticEditor.GamePass.Storage</c>) on its own terms:
/// injected filesystem/clock/log, typed results for lock conflicts, concurrent changes and
/// unsupported layouts, write ordering under an interrupted commit, and the adapter hooks.
/// Everything runs against an in-memory filesystem, so no test touches the disk or the clock.
/// </summary>
public class GamePassStorageTests
{
    private const string Root = "/store";
    private const string Family = "Test.Game_abc!App";

    // ---- package boundary ---------------------------------------------------------------

    [Fact]
    public void The_storage_package_does_not_depend_on_core_or_any_abiotic_assembly()
    {
        var referenced = typeof(WgsStore).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(referenced, n => n!.StartsWith("AbioticEditor", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n!.Contains("UeSaveGame", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n!.Contains("CUE4Parse", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_forwarding_enums_carry_exactly_the_same_values_as_the_package()
    {
        static Dictionary<string, uint> Values<T>() where T : struct, Enum
            => Enum.GetNames<T>().ToDictionary(n => n, n => Convert.ToUInt32(Enum.Parse<T>(n), System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(Values<WgsEntryState>(), Values<CoreGp.WgsEntryState>());
        Assert.Equal(Values<WgsSyncState>(), Values<CoreGp.WgsSyncState>());
    }

    // ---- injected clock / log -----------------------------------------------------------

    [Fact]
    public void The_injected_clock_stamps_the_index_and_the_entry_and_the_index_never_goes_backwards()
    {
        var fs = new MemFs();
        var clock = new FixedClock(new DateTimeOffset(2030, 1, 2, 3, 4, 5, 678, TimeSpan.Zero));
        var options = Options(fs, clock);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);

        var store = WgsStore.Open(Root, options);
        var first = clock.Now.ToFileTime();
        Assert.Equal(first, store.IndexFileTime);
        Assert.Equal(first - first % 10_000, store.Containers[0].FileTime);

        // Same instant again: the stamp still advances by one tick.
        store.WriteBlob(store.Containers[0], Bytes(60, 2));
        Assert.Equal(first + 1, store.IndexFileTime);

        // A clock that jumped backwards must not make the index look older.
        clock.Now = clock.Now.AddDays(-30);
        store.WriteBlob(store.Containers[0], Bytes(61, 3));
        Assert.Equal(first + 2, store.IndexFileTime);

        // A clock that moves forward is honoured.
        clock.Now = clock.Now.AddDays(60);
        store.WriteBlob(store.Containers[0], Bytes(62, 4));
        Assert.Equal(clock.Now.ToFileTime(), store.IndexFileTime);
    }

    [Fact]
    public void The_injected_log_receives_what_the_store_does()
    {
        var fs = new MemFs();
        var log = new ListLog();
        var options = new WgsStoreOptions { FileSystem = fs, Log = log };
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var store = WgsStore.Open(Root, options);

        store.WriteBlob(store.Containers[0], Bytes(51, 2));

        Assert.Contains(log.Lines, l => l.Contains("wrote container 'World-WC'", StringComparison.Ordinal));
    }

    // ---- typed open results -------------------------------------------------------------

    [Fact]
    public void Opening_reports_a_missing_index_an_unreadable_index_and_a_locked_index_as_typed_results()
    {
        var fs = new MemFs();
        var options = Options(fs);

        Assert.Equal(WgsOpenStatus.NotAContainerFolder, WgsStore.TryOpen(Root, options).Status);

        fs.Files[$"{Root}/containers.index"] = [1, 2, 3];
        var bad = WgsStore.TryOpen(Root, options);
        Assert.Equal(WgsOpenStatus.UnsupportedLayout, bad.Status);
        Assert.Null(bad.Store);
        Assert.NotEmpty(bad.Message!);

        fs.Files.Clear();
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(10, 1), Family, options);
        fs.Fault = (op, path) => op == "ReadAllBytes" && path.EndsWith("containers.index", StringComparison.Ordinal)
            ? SharingViolation() : null;
        Assert.Equal(WgsOpenStatus.LockConflict, WgsStore.TryOpen(Root, options).Status);

        fs.Fault = null;
        var ok = WgsStore.TryOpen(Root, options);
        Assert.True(ok.Succeeded);
        Assert.Equal(Family, ok.Store!.PackageFamilyName);
        Assert.Equal(WgsStore.KnownIndexVersion, ok.Store.IndexVersion);
    }

    // ---- concurrent change --------------------------------------------------------------

    [Fact]
    public void A_store_changed_by_someone_else_after_inspection_is_refused_before_anything_is_written()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var mine = WgsStore.Open(Root, options);
        var theirs = WgsStore.Open(Root, options);   // the game / sync client

        Assert.False(mine.DetectExternalChange().Changed);
        theirs.WriteBlob(theirs.Containers[0], Bytes(70, 9));
        var filesBefore = fs.Files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(mine.DetectExternalChange().Changed);
        var result = mine.TryWriteBlob(mine.Containers[0], Bytes(80, 2));

        Assert.Equal(WgsOperationStatus.ConcurrentChange, result.Status);
        Assert.Equal(filesBefore, fs.Files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());

        // Re-open, re-evaluate, and the same edit goes through.
        var fresh = WgsStore.Open(Root, options);
        Assert.Equal(WgsOperationStatus.Ok, fresh.TryWriteBlob(fresh.Containers[0], Bytes(80, 2)).Status);
        // After its own commit an instance is current again, so it can keep writing.
        Assert.False(fresh.DetectExternalChange().Changed);
        Assert.Equal(WgsOperationStatus.Ok, fresh.TryWriteBlob(fresh.Containers[0], Bytes(81, 3)).Status);
    }

    // ---- lock conflicts and interrupted commits -----------------------------------------

    [Fact]
    public void An_interrupted_commit_leaves_the_previous_generation_fully_described()
    {
        var fs = new MemFs();
        var options = Options(fs);
        var original = Bytes(50, 1);
        WgsStore.WriteNewContainer(Root, "World-WC", original, Family, options);
        var store = WgsStore.Open(Root, options);

        // The index replace is the last step that matters; make it fail as a locked file would.
        fs.Fault = (op, path) => op == "MoveOverwrite" && path.EndsWith("containers.index", StringComparison.Ordinal)
            ? SharingViolation() : null;
        var result = store.TryWriteBlob(store.Containers[0], Bytes(90, 2));
        fs.Fault = null;

        Assert.Equal(WgsOperationStatus.LockConflict, result.Status);
        var reopened = WgsStore.Open(Root, options);
        Assert.Equal((byte)1, reopened.Containers[0].ContainerNumber);
        Assert.Equal(original, reopened.ReadBlob(reopened.Containers[0]));
        Assert.DoesNotContain(fs.Files.Keys, k => k.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Access_denied_is_a_lock_conflict_and_other_io_errors_are_plain_failures()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var store = WgsStore.Open(Root, options);

        fs.Fault = (op, _) => op == "WriteAllBytes" ? new UnauthorizedAccessException("denied") : null;
        Assert.Equal(WgsOperationStatus.LockConflict, store.TryWriteBlob(store.Containers[0], Bytes(5, 1)).Status);

        fs.Fault = (op, _) => op == "WriteAllBytes" ? new IOException("disk full") : null;
        Assert.Equal(WgsOperationStatus.Failed, store.TryWriteBlob(store.Containers[0], Bytes(5, 1)).Status);
    }

    // ---- write gate ---------------------------------------------------------------------

    [Fact]
    public void The_default_gate_refuses_an_unresolved_conflict_and_reports_it_as_a_typed_result()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        PatchSyncFlags(fs, (uint)WgsSyncState.HasUnresolvedConflicts);
        var store = WgsStore.Open(Root, options);
        var before = fs.Snapshot();

        var result = store.TryWriteBlob(store.Containers[0], Bytes(60, 2));

        Assert.Equal(WgsOperationStatus.Refused, result.Status);
        Assert.Contains(result.Assessment!.Concerns, c => c.Code == WgsWriteGates.UnresolvedConflict && c.Blocking);
        Assert.Equal(before, fs.Snapshot());
        Assert.Throws<WgsWriteRefusedException>(() => store.WriteBlob(store.Containers[0], Bytes(60, 2)));
    }

    [Fact]
    public void A_platform_gate_can_add_its_own_concerns()
    {
        var fs = new MemFs();
        var gate = new DenyGate("game-running", "The game is running.");
        var options = new WgsStoreOptions { FileSystem = fs, WriteGate = gate };
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var store = WgsStore.Open(Root, options);

        var result = store.TryAddOrReplaceContainer("Other-WC", Bytes(5, 1));

        Assert.Equal(WgsOperationStatus.Refused, result.Status);
        Assert.Equal("The game is running.", result.Message);
        Assert.Single(store.Containers);
    }

    // ---- typed reads --------------------------------------------------------------------

    [Fact]
    public void Reading_reports_missing_blobs_in_flight_syncs_and_unsupported_manifests_as_typed_results()
    {
        var fs = new MemFs();
        var options = Options(fs);
        var blob = Bytes(64, 1);
        WgsStore.WriteNewContainer(Root, "World-WC", blob, Family, options);
        var store = WgsStore.Open(Root, options);
        var c = store.Containers[0];
        var folder = $"{Root}/{c.FolderName}";
        var blobKey = fs.Files.Keys.Single(k => k.StartsWith(folder + "/", StringComparison.Ordinal) && !k.EndsWith("container.1", StringComparison.Ordinal));
        var manifestKey = $"{folder}/container.1";

        Assert.Equal(blob, store.TryReadBlob(c).Blob);

        // Two different blobs on disk: a sync is in flight, and nothing says which wins.
        var manifest = fs.Files[manifestKey].ToArray();
        var other = Guid.NewGuid();
        Array.Copy(other.ToByteArray(), 0, manifest, 8 + 128, 16);   // previous id differs from current
        fs.Files[manifestKey] = manifest;
        fs.Files[$"{folder}/{other.ToString("N").ToUpperInvariant()}"] = Bytes(64, 2);
        Assert.Equal(WgsOperationStatus.SyncInFlight, store.TryReadBlob(c).Status);
        Assert.Contains(c.Name, store.RecoveredContainers);

        // Current blob gone, the manifest's previous id present: a recorded alternative is used.
        fs.Files.Remove(blobKey);
        var viaPrevious = store.TryReadBlob(c);
        Assert.True(viaPrevious.Succeeded);
        Assert.True(viaPrevious.UsedFallback);

        // Nothing usable left.
        fs.Files.Remove(fs.Files.Keys.Single(k => k.StartsWith(folder + "/", StringComparison.Ordinal) && !k.EndsWith("container.1", StringComparison.Ordinal)));
        Assert.Equal(WgsOperationStatus.MissingBlob, store.TryReadBlob(c).Status);

        // A manifest declaring more than one blob is a layout this package does not model.
        var multi = fs.Files[manifestKey].ToArray();
        BitConverter.GetBytes(2u).CopyTo(multi, 4);
        fs.Files[manifestKey] = multi;
        Assert.Equal(WgsOperationStatus.UnsupportedLayout, store.TryReadBlob(c).Status);
        Assert.Equal([c.Name], store.Diagnose().MultiBlobContainers);

        // A truncated manifest is unsupported too, not an exception.
        fs.Files[manifestKey] = [4, 0, 0, 0];
        Assert.Equal(WgsOperationStatus.UnsupportedLayout, store.TryReadBlob(c).Status);
    }

    // ---- planning and diagnosis ---------------------------------------------------------

    [Fact]
    public void Planning_a_write_previews_the_steps_and_touches_nothing()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var store = WgsStore.Open(Root, options);
        var before = fs.Snapshot();

        var plan = store.PlanWrite(store.Containers[0], 1234);

        Assert.Equal((byte)1, plan.CurrentNumber);
        Assert.Equal((byte)2, plan.NewNumber);
        Assert.Equal(WgsEntryState.Created, plan.NewState);   // never uploaded: no ETag
        Assert.Equal(1234, plan.NewBlobSize);
        Assert.Equal(2, plan.FilesToRemove.Count);            // the old manifest and blob
        Assert.Contains("container.1", plan.FilesToRemove);
        Assert.True(plan.Assessment.CanWrite);
        Assert.Equal(4, plan.Steps.Count);
        Assert.Equal(before, fs.Snapshot());
    }

    [Fact]
    public void Diagnosing_a_store_reports_health_without_repairing_anything()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        PatchSyncFlags(fs, (uint)WgsSyncState.HasUnresolvedConflicts);
        var store = WgsStore.Open(Root, options);
        var before = fs.Snapshot();

        var d = store.Diagnose();

        Assert.True(d.IsKnownIndexVersion);
        Assert.Equal(Family, d.PackageFamilyName);
        Assert.Equal(1, d.ContainerCount);
        Assert.Contains(WgsStore.CloudConflictLabel, d.ContainersNeedingRepair);
        Assert.False(d.WriteAssessment.CanWrite);
        Assert.Equal(before, fs.Snapshot());   // a diagnosis never repairs

        // Repair is an explicit act, and it clears the marker.
        Assert.Contains(WgsStore.CloudConflictLabel, store.RepairRecoveredManifests());
        Assert.False(store.HasUnresolvedConflicts);
    }

    // ---- adapter hooks ------------------------------------------------------------------

    [Fact]
    public void A_blob_inspector_supplied_by_a_game_adapter_labels_orphaned_data()
    {
        var fs = new MemFs();
        var inspector = new PrefixInspector("GAME1");
        var options = new WgsStoreOptions { FileSystem = fs, BlobInspector = inspector };
        WgsStore.WriteNewContainer(Root, "Live-WC", Bytes(50, 1), Family, options);

        // Orphaned folder: a manifest plus a blob no index entry names.
        var orphanFolder = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var blobId = Guid.NewGuid();
        var payload = Encoding.ASCII.GetBytes("GAME1:Save42:rest-of-blob");
        fs.Files[$"{Root}/{orphanFolder}/{blobId.ToString("N").ToUpperInvariant()}"] = payload;
        fs.Files[$"{Root}/{orphanFolder}/container.4"] = Manifest(blobId);

        var found = Assert.Single(WgsStore.FindOrphanedContainers(Root, options));

        Assert.Equal("Save42", found.Label);
        Assert.Equal("Save42-SLOT", found.SuggestedContainerName);
        Assert.Equal(payload.Length, found.BlobSize);
        Assert.Equal(inspector.HeadBytes, inspector.LastHeadLength);

        // Without an adapter the data is still found, just unlabelled.
        var bare = Assert.Single(WgsStore.FindOrphanedContainers(Root, new WgsStoreOptions { FileSystem = fs }));
        Assert.Null(bare.Label);
    }

    [Fact]
    public void A_snapshot_taken_through_the_package_notices_a_write()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(50, 1), Family, options);
        var store = WgsStore.Open(Root, options);
        var before = WgsSnapshot.Capture(store);

        store.WriteBlob(store.Containers[0], Bytes(51, 2));
        var after = WgsSnapshot.Capture(store);

        var diff = WgsSnapshot.Compare(before, after);
        Assert.Contains(diff, l => l.StartsWith("CHANGED", StringComparison.Ordinal));
        Assert.Empty(WgsSnapshot.Compare(after, after));
    }

    [Fact]
    public void Copying_a_store_produces_a_byte_identical_backup()
    {
        var fs = new MemFs();
        var options = Options(fs);
        WgsStore.WriteNewContainer(Root, "World-WC", Bytes(500, 1), Family, options);
        var store = WgsStore.Open(Root, options);

        store.CopyStoreTo("/backup");

        foreach (var (key, value) in fs.Files.Where(f => f.Key.StartsWith(Root + "/", StringComparison.Ordinal)).ToList())
        {
            Assert.Equal(value, fs.Files["/backup" + key[Root.Length..]]);
        }
    }

    [Fact]
    public void The_package_works_against_the_real_filesystem_too()
    {
        var dir = Directory.CreateTempSubdirectory("abiotic-wgs-storage-");
        try
        {
            var path = Path.Combine(dir.FullName, "wgs");
            WgsStore.WriteNewContainer(path, "World-WC", Bytes(100, 1), Family);
            var open = WgsStore.TryOpen(path);
            Assert.True(open.Succeeded);
            Assert.Equal(WgsOperationStatus.Ok, open.Store!.TryWriteBlob(open.Store.Containers[0], Bytes(120, 2)).Status);
            Assert.Equal(Bytes(120, 2), WgsStore.Open(path).ReadBlob(WgsStore.Open(path).Containers[0]));
        }
        finally
        {
            try { dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    private static WgsStoreOptions Options(MemFs fs, IWgsClock? clock = null)
        => new() { FileSystem = fs, Clock = clock ?? new FixedClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)) };

    private static IOException SharingViolation() => new("in use", unchecked((int)0x80070020));

    private static byte[] Bytes(int length, int seed)
    {
        var b = new byte[length];
        new Random(seed).NextBytes(b);
        return b;
    }

    private static byte[] Manifest(Guid blob)
    {
        var m = new byte[8 + 128 + 32];
        BitConverter.GetBytes(4u).CopyTo(m, 0);
        BitConverter.GetBytes(1u).CopyTo(m, 4);
        blob.ToByteArray().CopyTo(m, 8 + 128);
        blob.ToByteArray().CopyTo(m, 8 + 128 + 16);
        return m;
    }

    private static void PatchSyncFlags(MemFs fs, uint flags)
    {
        var key = $"{Root}/containers.index";
        var bytes = fs.Files[key].ToArray();
        var flagsAt = 16 + (int)BitConverter.ToUInt32(bytes, 12) * 2 + 8;
        BitConverter.GetBytes(flags).CopyTo(bytes, flagsAt);
        fs.Files[key] = bytes;
    }

    private sealed class FixedClock(DateTimeOffset now) : IWgsClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class ListLog : IWgsLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warn(string message) => Lines.Add(message);
    }

    private sealed class DenyGate(string code, string message) : IWgsWriteGate
    {
        public WgsWriteAssessment Assess(WgsStore store) => new([new WgsWriteConcern(code, true, message)]);
    }

    private sealed class PrefixInspector(string prefix) : IWgsBlobInspector
    {
        public int LastHeadLength { get; private set; }
        public int HeadBytes => 64;

        public WgsBlobDescription? Inspect(ReadOnlySpan<byte> head)
        {
            LastHeadLength = head.Length == 0 ? 0 : HeadBytes;
            var text = Encoding.ASCII.GetString(head);
            if (!text.StartsWith(prefix + ":", StringComparison.Ordinal)) return null;
            var label = text[(prefix.Length + 1)..].Split(':')[0];
            return new WgsBlobDescription(label, label + "-SLOT");
        }
    }

    /// <summary>An in-memory filesystem with an optional fault hook, keyed by '/'-normalised path.</summary>
    private sealed class MemFs : IWgsFileSystem
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        private readonly HashSet<string> _dirs = new(StringComparer.Ordinal);

        /// <summary>Called before each operation with (operation, path); a returned exception is thrown.</summary>
        public Func<string, string, Exception?>? Fault { get; set; }

        private static string N(string p) => p.Replace('\\', '/').TrimEnd('/');

        private void Check(string op, string path)
        {
            if (Fault?.Invoke(op, N(path)) is { } ex) throw ex;
        }

        public Dictionary<string, string> Snapshot()
            => Files.ToDictionary(f => f.Key, f => Convert.ToHexString(f.Value));

        public bool FileExists(string path) => Files.ContainsKey(N(path));
        public bool DirectoryExists(string path) => _dirs.Contains(N(path)) || Files.Keys.Any(k => k.StartsWith(N(path) + "/", StringComparison.Ordinal));

        public byte[] ReadAllBytes(string path)
        {
            Check("ReadAllBytes", path);
            return Files.TryGetValue(N(path), out var b) ? b.ToArray() : throw new FileNotFoundException(path);
        }

        public byte[] ReadHead(string path, int count)
        {
            Check("ReadHead", path);
            return Files.TryGetValue(N(path), out var b) ? b.Take(count).ToArray() : throw new FileNotFoundException(path);
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            Check("WriteAllBytes", path);
            Files[N(path)] = bytes.ToArray();
        }

        public void MoveOverwrite(string source, string destination)
        {
            Check("MoveOverwrite", destination);
            if (!Files.Remove(N(source), out var b)) throw new FileNotFoundException(source);
            Files[N(destination)] = b;
        }

        public void DeleteFile(string path)
        {
            Check("DeleteFile", path);
            Files.Remove(N(path));
        }

        public void CreateDirectory(string path) => _dirs.Add(N(path));

        public IEnumerable<string> EnumerateFiles(string directory)
        {
            var prefix = N(directory) + "/";
            return Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !k[prefix.Length..].Contains('/')).ToList();
        }

        public IEnumerable<string> EnumerateDirectories(string directory)
        {
            var prefix = N(directory) + "/";
            return Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k[prefix.Length..])
                .Where(r => r.Contains('/'))
                .Select(r => prefix + r.Split('/')[0])
                .Distinct().ToList();
        }

        public long GetFileLength(string path)
            => Files.TryGetValue(N(path), out var b) ? b.Length : throw new FileNotFoundException(path);

        public DateTime GetLastWriteTimeUtc(string path) => new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
