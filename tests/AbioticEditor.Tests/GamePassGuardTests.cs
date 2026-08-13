using System.Text;
using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// The checks that stand between an edit and a Game Pass save Xbox is going to argue with: the
/// write guard and the deliberate override, the game's own spare copy of a world, save data left
/// on disk after the container list forgot it, and telling a real account folder from the other
/// things that live in a <c>wgs</c> folder.
/// </summary>
public class GamePassGuardTests
{
    // ---- what the guard decides ------------------------------------------------------------
    //
    // Exercised through the decision itself rather than through a store, because the interesting
    // combinations (the game running, an unreadable process list) cannot be arranged on the
    // machine running the tests without making the result depend on what that machine is doing.

    [Fact]
    public void An_unsettled_cloud_conflict_refuses_the_write()
    {
        var check = GamePassWriteCheck.For(
            hasUnresolvedConflicts: true,
            unsafeStateContainers: [],
            contradictoryStateContainers: [],
            scan: GamePassProcessScan.Nothing,
            storeIsLive: true);

        Assert.False(check.CanWrite);
        Assert.Equal(GamePassWriteRisk.UnresolvedConflict, Assert.Single(check.Blockers).Risk);
        // The player has to be able to act on it, so the refusal names the way out.
        Assert.Contains("launch the game", check.BlockingMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gamepass repair", check.BlockingMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_container_the_service_may_take_away_refuses_the_write()
    {
        var check = GamePassWriteCheck.For(
            hasUnresolvedConflicts: false,
            unsafeStateContainers: ["World-WC"],
            contradictoryStateContainers: [],
            scan: GamePassProcessScan.Nothing,
            storeIsLive: false);

        Assert.False(check.CanWrite);
        Assert.Equal(GamePassWriteRisk.UnsafeContainerState, Assert.Single(check.Blockers).Risk);
        Assert.Contains("World-WC", check.BlockingMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_that_merely_disagrees_with_its_cloud_token_is_a_warning()
    {
        // Writing the container is what puts this right, so refusing to write would leave the save
        // stuck in the state the write was going to fix.
        var check = GamePassWriteCheck.For(
            hasUnresolvedConflicts: false,
            unsafeStateContainers: [],
            contradictoryStateContainers: ["World-WC"],
            scan: GamePassProcessScan.Nothing,
            storeIsLive: true);

        Assert.True(check.CanWrite);
        Assert.Equal(GamePassWriteRisk.ContradictoryContainerState, Assert.Single(check.Warnings).Risk);
    }

    [Fact]
    public void The_game_running_refuses_a_write_to_the_save_the_game_uses()
    {
        var check = GamePassWriteCheck.For(false, [], [], Running(GamePassProcessRole.Game), storeIsLive: true);

        Assert.False(check.CanWrite);
        Assert.Equal(GamePassWriteRisk.GameRunning, Assert.Single(check.Blockers).Risk);
    }

    [Fact]
    public void The_game_running_does_not_refuse_a_write_to_a_copy_of_a_save()
    {
        // A folder outside Connected Storage is not the container the running game holds open and
        // overwrites on exit, so the game being up says nothing about it.
        var check = GamePassWriteCheck.For(false, [], [], Running(GamePassProcessRole.Game), storeIsLive: false);

        Assert.True(check.CanWrite);
    }

    [Theory]
    [InlineData(GamePassProcessRole.XboxApp)]
    [InlineData(GamePassProcessRole.SyncService)]
    public void The_xbox_app_and_its_sync_service_only_warn(GamePassProcessRole role)
    {
        // Both run all day on a Game Pass machine (observed on a live install with the game closed),
        // so refusing on them would refuse every edit and teach players to force every save.
        var check = GamePassWriteCheck.For(false, [], [], Running(role), storeIsLive: true);

        Assert.True(check.CanWrite);
        Assert.Equal(GamePassWriteRisk.CompanionRunning, Assert.Single(check.Warnings).Risk);
    }

    [Fact]
    public void A_process_list_that_cannot_be_read_is_reported_rather_than_assumed_safe()
    {
        var scan = new GamePassProcessScan { Found = [], Unknown = true };
        var check = GamePassWriteCheck.For(false, [], [], scan, storeIsLive: true);

        Assert.False(scan.IsGameRunning);
        Assert.Contains(check.Warnings, c => c.Risk == GamePassWriteRisk.ProcessScanUnavailable);
    }

    [Fact]
    public void Looking_for_the_game_never_throws_and_says_what_it_found()
    {
        // Whatever this machine happens to be running, the scan itself must be safe to call from a
        // write path: an exception here would fail a save that had nothing wrong with it.
        var scan = GamePassEnvironment.Scan();

        Assert.NotNull(scan);
        Assert.All(scan.Found, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        Assert.Equal(scan.Found.Any(p => p.Role == GamePassProcessRole.Game), scan.IsGameRunning);
    }

    [Fact]
    public void A_throwaway_folder_is_not_mistaken_for_the_installed_games_save_area()
    {
        using var scratch = new Scratch();
        Directory.CreateDirectory(scratch.Path);

        Assert.False(GamePassEnvironment.IsInsideConnectedStorage(scratch.Path));
        Assert.False(GamePassEnvironment.IsInsideConnectedStorage(null));
        Assert.False(GamePassEnvironment.IsInsideConnectedStorage("   "));
    }

    // ---- the guard on a real store ---------------------------------------------------------

    [Fact]
    public void A_save_with_an_unsettled_conflict_refuses_every_write()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(1024, seed: 70));
        PatchSyncFlags(scratch.Path, (uint)(WgsSyncState.FullyUploaded | WgsSyncState.HasUnresolvedConflicts));

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.True(store.HasUnresolvedConflicts);
        Assert.False(store.CheckWritable().CanWrite);

        Assert.Throws<GamePassUnsafeWriteException>(
            () => store.WriteBlob(store.Containers[0], Payload(1024, seed: 71)));
        Assert.Throws<GamePassUnsafeWriteException>(
            () => store.AddOrReplaceContainer("Other-WC", Payload(1024, seed: 72)));
    }

    [Fact]
    public void A_refused_write_leaves_the_save_exactly_as_it_was()
    {
        using var scratch = new Scratch();
        var original = Payload(2048, seed: 73);
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", original);
        PatchSyncFlags(scratch.Path, (uint)WgsSyncState.HasUnresolvedConflicts);
        var indexBefore = File.ReadAllBytes(Path.Combine(scratch.Path, "containers.index"));

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Throws<GamePassUnsafeWriteException>(
            () => store.WriteBlob(store.Containers[0], Payload(2048, seed: 74)));

        // Nothing written means nothing to undo: same index, same single blob, same contents.
        Assert.Equal(indexBefore, File.ReadAllBytes(Path.Combine(scratch.Path, "containers.index")));
        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(original, reopened.ReadBlob(reopened.Containers[0]));
    }

    [Fact]
    public void An_informed_caller_can_accept_the_risk_and_write_anyway()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(1024, seed: 75));
        PatchSyncFlags(scratch.Path, (uint)WgsSyncState.HasUnresolvedConflicts);
        var edited = Payload(1500, seed: 76);

        var store = WgsContainerStore.Open(scratch.Path);
        store.AllowUnsafeWrites(GamePassWriteOverride.AcceptRiskOfLosingThisSave("test confirmed the warning"));
        store.WriteBlob(store.Containers[0], edited);

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(edited, reopened.ReadBlob(reopened.Containers[0]));
        // The acceptance covers the store it was given to and nothing else, so the next save the
        // editor opens starts from a refusal again.
        Assert.Null(reopened.WriteOverride);
        Assert.Throws<GamePassUnsafeWriteException>(
            () => reopened.WriteBlob(reopened.Containers[0], Payload(16, seed: 77)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void The_risk_can_only_be_accepted_by_saying_who_accepted_it(string? reason)
    {
        // The override exists to be deliberate. One that can be constructed with nothing to say is
        // one a careless caller trips without noticing.
        Assert.ThrowsAny<ArgumentException>(
            () => GamePassWriteOverride.AcceptRiskOfLosingThisSave(reason!));
    }

    [Fact]
    public void A_part_of_the_save_marked_deleted_refuses_a_write_until_it_is_repaired()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(512, seed: 78));
        PatchFirstEntryState(scratch.Path, (uint)WgsEntryState.Deleted);

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Equal("World-WC", Assert.Single(store.UnsafeStateContainers));
        Assert.Throws<GamePassUnsafeWriteException>(
            () => store.WriteBlob(store.Containers[0], Payload(512, seed: 79)));

        // Repair is the way out, and it must not itself be blocked by the state it exists to fix.
        Assert.Contains("World-WC", store.RepairRecoveredManifests());
        var repaired = WgsContainerStore.Open(scratch.Path);
        Assert.Empty(repaired.UnsafeStateContainers);
        repaired.WriteBlob(repaired.Containers[0], Payload(600, seed: 80));
    }

    [Fact]
    public void Editing_a_healthy_save_is_not_refused()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(256, seed: 81));

        var store = WgsContainerStore.Open(scratch.Path);
        var check = store.CheckWritable();

        Assert.True(check.CanWrite);
        Assert.Empty(check.Blockers);
        store.WriteBlob(store.Containers[0], Payload(300, seed: 82));
    }

    // ---- the game's own spare copy of a world ----------------------------------------------

    [Fact]
    public void The_games_backup_copy_is_offered_as_a_backup_not_as_a_second_world()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC", WorldBundle("Cascade", padding: 400));
        WgsContainerStore.Open(scratch.Path).AddOrReplaceContainer("Cascade-WC-B", WorldBundle("Cascade", padding: 300));

        var backup = Assert.Single(GamePassSaveSet.Open(scratch.Path).WorldBackups());

        // Naming it as the same world under a separate heading is the point: merged into the world
        // list it would be a second "Cascade" and nobody could tell which one they were editing.
        Assert.Equal("Cascade", backup.WorldName);
        Assert.Equal("Cascade-WC-B", backup.ContainerName);
        Assert.Equal("Cascade-WC", backup.LiveContainerName);
        Assert.True(backup.LiveWorldExists);
    }

    [Fact]
    public void Restoring_the_backup_puts_that_copy_back_over_the_live_world()
    {
        using var scratch = new Scratch();
        var live = WorldBundle("Cascade", padding: 400);
        var spare = WorldBundle("Cascade", padding: 300);
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC", live);
        WgsContainerStore.Open(scratch.Path).AddOrReplaceContainer("Cascade-WC-B", spare);

        var set = GamePassSaveSet.Open(scratch.Path);
        Assert.Equal("Cascade", set.RestoreWorldFromBackup("Cascade-WC-B"));

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(spare, reopened.ReadBlob(reopened.Find("Cascade-WC")!));
        // The spare copy itself is untouched, so restoring twice is not a one-way door.
        Assert.Equal(spare, reopened.ReadBlob(reopened.Find("Cascade-WC-B")!));
        // Everything being replaced is still recoverable, because the folder is copied first.
        Assert.NotEmpty(Directory.GetDirectories(
            Path.GetDirectoryName(scratch.Path)!, Path.GetFileName(scratch.Path) + ".bak*"));
    }

    [Fact]
    public void A_world_whose_live_copy_is_gone_can_still_be_restored_from_the_backup()
    {
        using var scratch = new Scratch();
        var spare = WorldBundle("Cascade", padding: 250);
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC-B", spare);

        var set = GamePassSaveSet.Open(scratch.Path);
        Assert.False(Assert.Single(set.WorldBackups()).LiveWorldExists);
        set.RestoreWorldFromBackup("Cascade-WC-B");

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(spare, reopened.ReadBlob(reopened.Find("Cascade-WC")!));
    }

    [Fact]
    public void Restoring_something_that_is_not_a_world_is_refused_before_anything_is_written()
    {
        using var scratch = new Scratch();
        var live = WorldBundle("Cascade", padding: 100);
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC", live);
        WgsContainerStore.Open(scratch.Path).AddOrReplaceContainer("Cascade-WC-B", Payload(900, seed: 83));

        var set = GamePassSaveSet.Open(scratch.Path);
        // Replacing a damaged world with bytes the game cannot open at all is not a recovery.
        Assert.Throws<InvalidDataException>(() => set.RestoreWorldFromBackup("Cascade-WC-B"));

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(live, reopened.ReadBlob(reopened.Find("Cascade-WC")!));
    }

    [Fact]
    public void Only_a_backup_container_can_be_restored()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC", WorldBundle("Cascade", padding: 100));

        var set = GamePassSaveSet.Open(scratch.Path);
        Assert.Throws<InvalidOperationException>(() => set.RestoreWorldFromBackup("Cascade-WC"));
        Assert.Throws<InvalidOperationException>(() => set.RestoreWorldFromBackup("Nothing-WC-B"));
    }

    // ---- save data the container list forgot -----------------------------------------------

    [Fact]
    public void Leftover_save_data_is_listed_with_enough_detail_to_recognise_it()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Other-WC", Payload(128, seed: 84));
        var blob = WorldBundle("Cascade", padding: 512);
        PlantOrphan(scratch.Path, blob);

        var orphan = Assert.Single(WgsContainerStore.FindOrphanedContainers(scratch.Path));

        Assert.Equal(1, orphan.ContainerNumber);
        Assert.Equal(blob.Length, orphan.BlobSize);
        // The world name comes out of the data's own contents list, which is not compressed - so a
        // player can be told which world they are about to get back even with no Oodle library.
        Assert.Equal("Cascade", orphan.WorldName);
        Assert.Equal("Cascade-WC", orphan.SuggestedContainerName);
    }

    [Fact]
    public void Putting_leftover_save_data_back_makes_that_world_visible_again()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Other-WC", Payload(128, seed: 85));
        var blob = WorldBundle("Cascade", padding: 700);
        PlantOrphan(scratch.Path, blob);

        var set = GamePassSaveSet.Open(scratch.Path);
        Assert.Equal("Cascade-WC", set.RecoverOrphanedWorld(set.OrphanedContainers()[0]));

        var reopened = WgsContainerStore.Open(scratch.Path);
        var recovered = reopened.Find("Cascade-WC")!;
        Assert.Equal(blob, reopened.ReadBlob(recovered));
        // The cloud's version token went with the index entry that named it, so the container has
        // to say it has never been uploaded rather than claim a version Xbox never issued.
        Assert.Equal(WgsEntryState.Created, recovered.State);
        Assert.Equal(string.Empty, recovered.Etag);
        Assert.False(recovered.StateContradictsEtag);
        Assert.Empty(reopened.OrphanedContainers());
    }

    [Fact]
    public void Putting_leftover_data_back_under_a_name_already_in_use_is_refused()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Cascade-WC", WorldBundle("Cascade", padding: 64));
        PlantOrphan(scratch.Path, WorldBundle("Cascade", padding: 128));

        var store = WgsContainerStore.Open(scratch.Path);
        var orphan = Assert.Single(store.OrphanedContainers());

        // The live world of that name is still there, so this leftover is an older copy of it, not
        // the missing one - and no name is suggested that would only be refused.
        Assert.Null(orphan.SuggestedContainerName);
        Assert.Throws<InvalidOperationException>(() => store.ReRegisterOrphan(orphan));
        Assert.Throws<InvalidOperationException>(() => store.ReRegisterOrphan(orphan, "Cascade-WC"));

        store.ReRegisterOrphan(orphan, "Cascade-Recovered-WC");
        Assert.NotNull(WgsContainerStore.Open(scratch.Path).Find("Cascade-Recovered-WC"));
    }

    [Fact]
    public void An_empty_leftover_folder_is_not_offered_as_recoverable()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "Other-WC", Payload(128, seed: 86));

        // A manifest whose blob is gone has nothing behind it, and offering to restore it would
        // just move the disappointment further down the line.
        var folder = Path.Combine(scratch.Path, Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "container.1"), ManifestBytes(Guid.NewGuid()));

        Assert.Empty(WgsContainerStore.FindOrphanedContainers(scratch.Path));
        Assert.False(WgsContainerStore.HasOrphanedWorldFolders(scratch.Path));
    }

    // ---- what else lives in a wgs folder ---------------------------------------------------

    [Theory]
    // Connected Storage's own working folder, seen next to the account folder on a live install.
    [InlineData("t")]
    // This editor's snapshots, which stack up with a timestamp and can end up doubled.
    [InlineData("000901FB9727E122_00000000000000000000000078A5.bak")]
    [InlineData("000901FB9727E122_00000000000000000000000078A5.bak-134270016777626435")]
    [InlineData("000901FB9727E122_00000000000000000000000078A5.bak.bak")]
    // Shapes other tools and the Xbox app are reported to leave behind.
    [InlineData("wgs-backup")]
    [InlineData("000901FB9727E122_00000000000000000000000078A5 - Copy")]
    [InlineData("000901FB9727E122_00000000000000000000000078A5.old")]
    [InlineData("~staging")]
    public void Things_in_a_wgs_folder_that_are_not_the_live_save_are_ignored(string folderName)
    {
        Assert.NotNull(GamePassDiscovery.IgnoredFolderReason(folderName));
    }

    [Theory]
    [InlineData("000901FB9727E122_0000000000000000000000007B483EAA")]
    [InlineData("00090000068E7E8D_0000000000000000000000007B483EAA")]
    public void A_real_account_folder_is_kept(string folderName)
    {
        Assert.Null(GamePassDiscovery.IgnoredFolderReason(folderName));
        Assert.True(GamePassDiscovery.IsAccountFolderName(folderName));
    }

    [Theory]
    [InlineData("wgs")]
    [InlineData("notanaccount")]
    [InlineData("000901FB9727E122")]
    [InlineData("zzzz_0000")]
    [InlineData("_00000000")]
    public void A_folder_that_is_not_named_like_an_account_is_recognised_as_such(string folderName)
    {
        // Not a reason to throw it away on its own: a save that plainly reads has to stay visible
        // even when its folder is named in a way nobody anticipated.
        Assert.False(GamePassDiscovery.IsAccountFolderName(folderName));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static GamePassProcessScan Running(GamePassProcessRole role) => new()
    {
        Found = [new GamePassRunningProcess(role == GamePassProcessRole.Game ? "AbioticFactor-Win64-Shipping" : "XboxPcApp", role)],
        Unknown = false,
    };

    /// <summary>
    /// A blob with a world bundle's header: the marker and the contents list, which the game leaves
    /// uncompressed at the front of the payload. That is all the store reads to identify a world, so
    /// nothing here needs an Oodle library - which is the point, since a player looking for a world
    /// they have lost is often on a machine that has none.
    /// </summary>
    private static byte[] WorldBundle(string world, int padding)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteFString(w, "ABF_SAVE_VERSION");
            w.Write(3);                       // bundle version
            w.Write(padding);                 // total uncompressed size
            w.Write(16);                      // opaque header field
            w.Write(1);                       // member count
            WriteFString(w, $"Profile/Worlds/{world}/WorldSave_MetaData");
            w.Write(padding);                 // member size
            WriteFString(w, "/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C");
            w.Write(0);                       // member flag
        }
        ms.Write(Payload(padding, seed: world.Length + padding));
        return ms.ToArray();
    }

    private static void WriteFString(BinaryWriter w, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        w.Write(bytes.Length + 1);
        w.Write(bytes);
        w.Write((byte)0);
    }

    /// <summary>
    /// Drops a GUID folder holding a manifest and its blob into the store without telling
    /// <c>containers.index</c> about it - the shape Xbox cloud sync leaves behind when it drops a
    /// world from the list but not from the disk.
    /// </summary>
    private static void PlantOrphan(string wgsFolder, byte[] blob)
    {
        var folder = Path.Combine(wgsFolder, Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(folder);
        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);
        File.WriteAllBytes(Path.Combine(folder, "container.1"), ManifestBytes(blobGuid));
    }

    /// <summary>A <c>container.N</c> manifest: a constant, one blob entry, the fixed 128-byte name
    /// field, then the blob id twice (as the cloud knew it, and as it is on disk).</summary>
    private static byte[] ManifestBytes(Guid blobGuid)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(4u);
        w.Write(1u);
        var nameField = new byte[128];
        Encoding.Unicode.GetBytes("Data").CopyTo(nameField, 0);
        w.Write(nameField);
        w.Write(blobGuid.ToByteArray());
        w.Write(blobGuid.ToByteArray());
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Sets the index-level sync flags, to stand in for a save Xbox has an unsettled
    /// conflict for. The editor never writes that bit itself, so it has to be made by hand.</summary>
    private static void PatchSyncFlags(string wgsFolder, uint flags)
    {
        var path = Path.Combine(wgsFolder, "containers.index");
        var d = File.ReadAllBytes(path);
        var pos = 12;                       // version + count + reserved
        SkipWideString(d, ref pos);         // package family name
        pos += 8;                           // index FILETIME
        BitConverter.GetBytes(flags).CopyTo(d, pos);
        File.WriteAllBytes(path, d);
    }

    /// <summary>Sets the first entry's container state directly, for the states the editor refuses
    /// to write and so cannot produce.</summary>
    private static void PatchFirstEntryState(string wgsFolder, uint state)
    {
        var path = Path.Combine(wgsFolder, "containers.index");
        var d = File.ReadAllBytes(path);
        var pos = 12;
        SkipWideString(d, ref pos);         // package family name
        pos += 8;                           // index FILETIME
        pos += 4;                           // sync flags
        SkipWideString(d, ref pos);         // root GUID
        pos += 8;                           // reserved
        SkipWideString(d, ref pos);         // entry name
        SkipWideString(d, ref pos);         // entry name (again)
        SkipWideString(d, ref pos);         // etag
        pos += 1;                           // container number
        BitConverter.GetBytes(state).CopyTo(d, pos);
        File.WriteAllBytes(path, d);
    }

    private static void SkipWideString(byte[] d, ref int pos)
    {
        var chars = BitConverter.ToUInt32(d, pos);
        pos += 4 + ((int)chars * 2);
    }

    private static byte[] Payload(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("abiotic-gp-guard-");
        public string Path => System.IO.Path.Combine(_dir.FullName, "wgs");
        public void Dispose()
        {
            try { _dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
