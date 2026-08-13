using System.Text;
using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// The Game Pass paths that touch a player's real Xbox saves and previously had no test at all:
/// recovering a container whose data blob is missing, repairing that state permanently, refusing
/// to write a container list over an existing save store, keeping old generations and backups from
/// piling up, and refusing a pack that would silently do nothing.
/// </summary>
public class GamePassSafetyTests
{
    private const string CharClass = GamePassMemberCodec.CharacterSaveClass;

    // ---- missing-blob fallback -------------------------------------------------------------

    [Fact]
    public void A_container_whose_blob_is_missing_is_recovered_from_the_one_on_disk()
    {
        using var scratch = new Scratch();
        var blob = Payload(4096, seed: 1);
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", blob);

        // Xbox left the manifest naming a blob that never arrived, while the real one sits next to
        // it under a different name. The save must still open.
        RenameBlob(scratch.Path, Guid.NewGuid());

        var store = WgsContainerStore.Open(scratch.Path);
        var read = store.ReadBlob(store.Containers[0]);

        Assert.Equal(blob, read);
        Assert.True(store.NeededBlobFallback);
        Assert.Equal("World-WC", Assert.Single(store.RecoveredContainers));
    }

    [Fact]
    public void A_wrong_sized_blob_is_never_substituted_for_the_missing_one()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(4096, seed: 2));

        // The only candidate is a different size, so it is data from some other moment - exactly
        // the case where quietly loading it would hand back the wrong save as if it were right.
        var folder = ContainerFolder(scratch.Path);
        foreach (var file in BlobFiles(folder)) File.Delete(file);
        File.WriteAllBytes(Path.Combine(folder, Guid.NewGuid().ToString("N").ToUpperInvariant()), Payload(512, seed: 3));

        var store = WgsContainerStore.Open(scratch.Path);
        var ex = Assert.Throws<InvalidDataException>(() => store.ReadBlob(store.Containers[0]));
        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_previous_blob_the_manifest_names_is_preferred_over_guessing()
    {
        using var scratch = new Scratch();
        var blob = Payload(4096, seed: 60);
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", blob);

        // A manifest records two ids: the blob as the cloud last knew it, and the one on disk. When
        // the current one is gone, the other is a name the save itself gives us, not a guess - so it
        // must win over scanning the folder, and it must win even against a same-sized decoy.
        var folder = ContainerFolder(scratch.Path);
        var real = BlobFiles(folder).Single();
        var previousGuid = Guid.NewGuid();
        File.Move(real, Path.Combine(folder, previousGuid.ToString("N").ToUpperInvariant()));
        var decoy = Payload(4096, seed: 61);
        File.WriteAllBytes(Path.Combine(folder, Guid.NewGuid().ToString("N").ToUpperInvariant()), decoy);
        PatchManifestPreviousGuid(folder, previousGuid);

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(blob, store.ReadBlob(store.Containers[0]));
        Assert.True(store.NeededBlobFallback);
    }

    [Fact]
    public void A_save_with_two_live_versions_of_its_data_refuses_to_guess()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(4096, seed: 62));

        // Both ids the manifest names exist and differ: a sync really is in flight, and nothing on
        // disk says which side is meant to win. Picking one would hand back the wrong save silently.
        var folder = ContainerFolder(scratch.Path);
        var previousGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, previousGuid.ToString("N").ToUpperInvariant()), Payload(4096, seed: 63));
        PatchManifestPreviousGuid(folder, previousGuid);

        var store = WgsContainerStore.Open(scratch.Path);
        var ex = Assert.Throws<InvalidDataException>(() => store.ReadBlob(store.Containers[0]));
        Assert.Contains("two versions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repairing_a_half_synced_save_makes_it_open_cleanly_again()
    {
        using var scratch = new Scratch();
        var blob = Payload(4096, seed: 4);
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", blob);
        RenameBlob(scratch.Path, Guid.NewGuid());

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(blob, store.ReadBlob(store.Containers[0]));   // needs the fallback
        Assert.True(store.NeededBlobFallback);

        var repaired = store.RepairRecoveredManifests();
        Assert.Equal("World-WC", Assert.Single(repaired));

        // Reopening is the real assertion: the manifest now names a blob that exists, so nothing
        // has to be recovered a second time.
        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(blob, reopened.ReadBlob(reopened.Containers[0]));
        Assert.False(reopened.NeededBlobFallback);
    }

    [Fact]
    public void Repairing_a_healthy_save_changes_nothing()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(2048, seed: 5));

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Empty(store.RepairRecoveredManifests());
        Assert.False(store.NeededBlobFallback);
    }

    // ---- writing ---------------------------------------------------------------------------

    [Fact]
    public void Writing_a_new_container_list_over_an_existing_save_store_is_refused()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(1024, seed: 6));

        // The index it would write describes one container, so it would orphan everything else.
        var ex = Assert.Throws<InvalidOperationException>(
            () => WgsContainerStore.WriteNewContainer(scratch.Path, "Other-WC", Payload(1024, seed: 7)));
        Assert.Contains("already an Xbox save folder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merging_adds_a_world_and_keeps_the_ones_already_there()
    {
        using var scratch = new Scratch();
        var first = Payload(1024, seed: 8);
        var second = Payload(2048, seed: 9);
        WgsContainerStore.WriteNewContainer(scratch.Path, "First-WC", first);

        WgsContainerStore.Open(scratch.Path).AddOrReplaceContainer("Second-WC", second);

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(2, store.Containers.Count);
        Assert.Equal(first, store.ReadBlob(store.Find("First-WC")!));
        Assert.Equal(second, store.ReadBlob(store.Find("Second-WC")!));
    }

    [Fact]
    public void Merging_over_an_existing_world_replaces_just_that_world()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(1024, seed: 10));
        var replacement = Payload(3000, seed: 11);

        WgsContainerStore.Open(scratch.Path).AddOrReplaceContainer("World-WC", replacement);

        var store = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(replacement, store.ReadBlob(Assert.Single(store.Containers)));
    }

    [Fact]
    public void Writing_leaves_exactly_one_generation_behind()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(1024, seed: 12));

        var store = WgsContainerStore.Open(scratch.Path);
        for (var i = 0; i < 3; i++) store.WriteBlob(store.Containers[0], Payload(1024 + i, seed: 20 + i));

        // Stale blobs in the folder are what make the missing-blob recovery ambiguous, so the
        // folder has to look like the game's own: one manifest, one blob.
        var folder = ContainerFolder(scratch.Path);
        Assert.Single(Directory.GetFiles(folder, "container.*"));
        Assert.Single(BlobFiles(folder));

        var reopened = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(Payload(1026, seed: 22), reopened.ReadBlob(reopened.Containers[0]));
        Assert.False(reopened.NeededBlobFallback);
    }

    [Fact]
    public void The_index_timestamp_never_goes_backwards_across_writes()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(512, seed: 13));

        var store = WgsContainerStore.Open(scratch.Path);
        var stamps = new List<long> { store.IndexFileTime };
        for (var i = 0; i < 4; i++)
        {
            store.WriteBlob(store.Containers[0], Payload(512, seed: 30 + i));
            stamps.Add(WgsContainerStore.Open(scratch.Path).IndexFileTime);
        }

        // Xbox picks the copy whose index reads newest, so an edit that lands on the same clock
        // tick as the last one still has to come out ahead of it.
        for (var i = 1; i < stamps.Count; i++) Assert.True(stamps[i] > stamps[i - 1], $"stamp {i} did not advance");
    }

    [Fact]
    public void A_container_that_has_never_been_uploaded_says_so_and_carries_no_cloud_token()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(256, seed: 40));

        var created = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(WgsEntryState.Created, created.Containers[0].State);
        Assert.Equal(string.Empty, created.Containers[0].Etag);
        Assert.False(created.Containers[0].StateContradictsEtag);

        created.WriteBlob(created.Containers[0], Payload(300, seed: 41));

        // Still never uploaded, so it stays Created rather than claiming to be a change to a cloud
        // version that does not exist.
        var after = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(WgsEntryState.Created, after.Containers[0].State);
        Assert.False(after.Containers[0].StateContradictsEtag);
        Assert.False(after.SyncState.HasFlag(WgsSyncState.FullyUploaded));
    }

    [Fact]
    public void Editing_a_synced_container_marks_it_modified_and_keeps_its_cloud_version_token()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(256, seed: 42));

        // Stand in for a container the service has synced: it carries an ETag Xbox issued, and it
        // currently agrees with the cloud.
        var seeded = WgsContainerStore.Open(scratch.Path);
        seeded.Containers[0].Etag = "\"0x8DEBCCC41BE9635\"";
        seeded.Containers[0].State = WgsEntryState.Synced;
        seeded.Containers[0].RawState = (uint)WgsEntryState.Synced;
        seeded.WriteBlob(seeded.Containers[0], Payload(400, seed: 43));

        var after = WgsContainerStore.Open(scratch.Path);

        // Modified, not Created: the cloud knows this container, and saying otherwise is what the
        // Palworld tools found the sync engine quietly discards.
        Assert.Equal(WgsEntryState.Modified, after.Containers[0].State);
        Assert.Equal("\"0x8DEBCCC41BE9635\"", after.Containers[0].Etag);
        Assert.False(after.Containers[0].StateContradictsEtag);
    }

    [Theory]
    [InlineData(7u)]    // past the end of the range entirely
    [InlineData(3u)]    // Deleted - a tombstone the service may act on
    public void A_container_left_in_a_state_that_makes_no_sense_is_repaired(uint badState)
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(256, seed: 44));

        // Earlier versions of this editor incremented the state on every save, so real saves in the
        // wild carry values like 6 and 7 - and, on the way there, Deleted(3).
        PatchFirstEntryState(scratch.Path, badState);

        var broken = WgsContainerStore.Open(scratch.Path);
        Assert.Contains("World-WC", broken.RepairRecoveredManifests());

        var repaired = WgsContainerStore.Open(scratch.Path);
        Assert.Empty(repaired.InvalidStateContainers);
        Assert.False(repaired.Containers[0].StateContradictsEtag);
    }

    [Fact]
    public void A_state_that_disagrees_with_the_cloud_token_is_reported_and_repaired()
    {
        using var scratch = new Scratch();
        WgsContainerStore.WriteNewContainer(scratch.Path, "World-WC", Payload(256, seed: 45));

        // Created(5) says "never uploaded", but an ETag says the cloud knows it. Two independently
        // written parsers reject that combination outright, so the editor must never leave one.
        var seeded = WgsContainerStore.Open(scratch.Path);
        seeded.Containers[0].Etag = "\"0x8DEBCCC41BE9635\"";
        seeded.WriteBlob(seeded.Containers[0], Payload(260, seed: 46));

        var after = WgsContainerStore.Open(scratch.Path);
        Assert.Equal(WgsEntryState.Modified, after.Containers[0].State);
        Assert.Empty(after.InvalidStateContainers);
    }

    // ---- bundle + member codec -------------------------------------------------------------

    [SkippableFact]
    public void A_world_name_with_accents_survives_the_bundle()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        // UE writes a non-ASCII FString as UTF-16 with a negative length. Reading only ASCII made
        // any world whose name is not plain English unreadable.
        const string path = "Profile/Worlds/Forschungsstation Ärger/WorldSave_MetaData";
        var member = new AbfMember { Path = path, SaveClass = CharClass, Flag = 0, Body = Payload(64, seed: 14) };

        var reparsed = AbfSaveBundle.Parse(AbfSaveBundle.Create(new[] { member }).Serialize());

        Assert.Equal(path, Assert.Single(reparsed.Members).Path);
    }

    [Fact]
    public void Sandbox_settings_text_survives_a_round_trip()
    {
        const string ini = "[SandboxSettings]\r\nGameDifficulty=3\r\nItemStackSizeMultiplier=30.0\r\n";
        Assert.Equal(ini, GamePassMemberCodec.DecodeIniText(GamePassMemberCodec.EncodeIniText(ini)));
    }

    [Fact]
    public void Sandbox_settings_are_stored_the_way_the_game_stores_them()
    {
        // Every byte is shifted down by one, which is why the packed member reads as gibberish
        // starting "ZR`mcanw". Getting this backwards would write settings the game cannot parse.
        var encoded = GamePassMemberCodec.EncodeIniText("[SandboxSettings]");
        Assert.Equal("ZR`mcanwRdsshmfr\\", Encoding.ASCII.GetString(encoded));
    }

    [Theory]
    [InlineData(GamePassMemberCodec.WorldSaveClass)]
    [InlineData(GamePassMemberCodec.WorldMetadataSaveClass)]
    [InlineData(GamePassMemberCodec.CharacterSaveClass)]
    public void Every_save_class_strips_and_restores_its_own_header(string saveClass)
    {
        // World and metadata saves carry a 33-byte custom header where a character save carries 8.
        // Only the character path had a test, leaving the constant that matters most unguarded.
        var body = Payload(2048, seed: 15);
        var gvas = GamePassMemberCodec.ToGvas(saveClass, body);

        Assert.Equal(body, GamePassMemberCodec.ToMemberBody(saveClass, gvas));
    }

    [Fact]
    public void An_unknown_save_class_is_refused_rather_than_guessed()
    {
        Assert.Throws<NotSupportedException>(
            () => GamePassMemberCodec.ToGvas("/Game/Blueprints/Saves/Something_Else_C", Payload(8, seed: 16)));
    }

    // ---- extracting and packing a world ----------------------------------------------------

    [SkippableFact]
    public void Packing_a_world_that_is_not_in_the_working_copy_is_refused()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");
        using var world = new PackedWorld();

        // The working copy is gone (cleaned up, moved, or never the one this world came from).
        // Packing would rewrite the container with its existing contents and report success, so
        // the player would be told their edit was saved when nothing was written.
        var empty = Directory.CreateTempSubdirectory("abiotic-gp-empty-");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => world.Set.ApplyWorld(PackedWorld.Container, empty.FullName));
            Assert.Contains("nothing to pack", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { empty.Delete(recursive: true); }
    }

    [SkippableFact]
    public void The_worlds_difficulty_settings_survive_extract_and_pack()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");
        using var world = new PackedWorld(withIni: true);

        var working = Directory.CreateTempSubdirectory("abiotic-gp-work-");
        try
        {
            world.Set.ExtractWorld(PackedWorld.Container, working.FullName);

            // The difficulty knobs live beside the saves rather than inside them, so a world that
            // extracts without them comes back on default difficulty.
            var ini = Path.Combine(working.FullName, "SandboxSettings.ini");
            Assert.True(File.Exists(ini), "SandboxSettings.ini should be extracted with the world");
            Assert.Contains("GameDifficulty=3", File.ReadAllText(ini), StringComparison.Ordinal);

            File.WriteAllText(ini, "[SandboxSettings]\r\nGameDifficulty=1\r\n");
            world.Set.ApplyWorld(PackedWorld.Container, working.FullName);

            var reopened = GamePassSaveSet.Open(world.Path);
            var reworking = Directory.CreateTempSubdirectory("abiotic-gp-work2-");
            try
            {
                reopened.ExtractWorld(PackedWorld.Container, reworking.FullName);
                Assert.Contains("GameDifficulty=1",
                    File.ReadAllText(Path.Combine(reworking.FullName, "SandboxSettings.ini")), StringComparison.Ordinal);
            }
            finally { reworking.Delete(recursive: true); }
        }
        finally { working.Delete(recursive: true); }
    }

    [SkippableTheory]
    [InlineData("../../../escaped/SandboxSettings.ini")]
    [InlineData("C:/Windows/Temp/SandboxSettings.ini")]
    [InlineData("/etc/SandboxSettings.ini")]
    public void A_member_named_to_escape_the_working_folder_stays_inside_it(string hostilePath)
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        // Member names are not trustworthy input: the game itself records the ini member under the
        // absolute path it had on the machine that wrote the save, so a save passed between players
        // routinely carries a rooted path, and a crafted one could carry a traversal.
        using var world = new PackedWorld(iniPath: hostilePath, withIni: true);
        var parent = Directory.CreateTempSubdirectory("abiotic-gp-slip-");
        var working = Path.Combine(parent.FullName, "working");
        try
        {
            world.Set.ExtractWorld(PackedWorld.Container, working);

            // Everything written lands under the working folder, named by its leaf only.
            var written = Directory.GetFiles(working, "*", SearchOption.AllDirectories);
            Assert.Contains(written, f => Path.GetFileName(f) == "SandboxSettings.ini");
            foreach (var file in written)
            {
                Assert.StartsWith(
                    Path.GetFullPath(working) + Path.DirectorySeparatorChar,
                    Path.GetFullPath(file),
                    StringComparison.OrdinalIgnoreCase);
            }
            Assert.False(Directory.Exists(Path.Combine(parent.FullName, "escaped")), "extraction escaped the working folder");
        }
        finally { parent.Delete(recursive: true); }
    }

    [SkippableFact]
    public void Old_save_backups_are_pruned_instead_of_piling_up()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");
        using var world = new PackedWorld();

        // Each editing session backs the whole folder up once, and a player edits the same world
        // many times. Without a cap those full copies accumulate next to their real saves forever.
        for (var i = 0; i < 12; i++)
        {
            var set = GamePassSaveSet.Open(world.Path);
            var entry = set.Entries().First(e => e.Kind == GamePassSaveKind.Player);
            set.WriteSave(entry, set.ReadSave(entry));
        }

        var parent = Path.GetDirectoryName(world.Path)!;
        var backups = Directory.GetDirectories(parent, Path.GetFileName(world.Path) + ".bak*");
        Assert.InRange(backups.Length, 1, 8);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// A throwaway wgs folder holding one world container, built from a real player fixture so the
    /// members are genuine saves rather than random bytes.
    /// </summary>
    private sealed class PackedWorld : IDisposable
    {
        public const string Container = "W-WC";
        private const string Ini = "[SandboxSettings]\r\nGameDifficulty=3\r\nItemStackSizeMultiplier=30.0\r\n";

        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("abiotic-gp-world-");

        public string Path { get; }
        public GamePassSaveSet Set { get; }

        public PackedWorld(bool withIni = false, string? iniPath = null)
        {
            Path = System.IO.Path.Combine(_root.FullName, "wgs");

            var player = File.ReadAllBytes(System.IO.Path.Combine(
                Fixtures.CascadeDir ?? throw new InvalidOperationException("the Steam world fixture is required"),
                "PlayerData", "Player_76561197993781479.sav"));

            var members = new List<AbfMember>
            {
                new()
                {
                    Path = "Profile/Worlds/W/PlayerData/Player_76561197993781479",
                    SaveClass = CharClass,
                    Flag = 0,
                    Body = GamePassMemberCodec.ToMemberBody(CharClass, player),
                },
            };
            if (withIni)
            {
                members.Add(new AbfMember
                {
                    Path = iniPath ?? "Profile/Worlds/W/SandboxSettings.ini",
                    SaveClass = string.Empty,
                    Flag = AbfMember.IniFlag,
                    Body = GamePassMemberCodec.EncodeIniText(Ini),
                });
            }

            WgsContainerStore.WriteNewContainer(Path, Container, AbfSaveBundle.Create(members).Serialize());
            Set = GamePassSaveSet.Open(Path);
        }

        public void Dispose()
        {
            try { _root.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }


    /// <summary>
    /// Overwrites the first entry's state directly in <c>containers.index</c>, to reproduce a save
    /// that a previous version of this editor left in a state the format does not define. The store
    /// will not write such a value any more, so the bytes are made by hand rather than by adding a
    /// production API that exists only for this.
    /// </summary>
    private static void PatchFirstEntryState(string wgsFolder, uint state)
    {
        var path = Path.Combine(wgsFolder, "containers.index");
        var d = File.ReadAllBytes(path);
        var pos = 12;                       // version + count + reserved
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

    /// <summary>
    /// Rewrites the first blob id in a <c>container.N</c> manifest (the one naming the blob as the
    /// cloud last knew it), leaving the second - the file on disk - alone. The store only ever
    /// writes the two identical, so an in-flight manifest has to be built by hand.
    /// </summary>
    private static void PatchManifestPreviousGuid(string containerFolder, Guid previous)
    {
        var manifest = Directory.GetFiles(containerFolder, "container.*").Single();
        var d = File.ReadAllBytes(manifest);
        previous.ToByteArray().CopyTo(d, 8 + 128);
        File.WriteAllBytes(manifest, d);
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

    private static string ContainerFolder(string root)
        => Directory.EnumerateDirectories(root).Single();

    private static IEnumerable<string> BlobFiles(string folder)
        => Directory.EnumerateFiles(folder)
            .Where(f => Path.GetFileName(f).Length == 32);

    /// <summary>Renames the container's blob so the manifest points at a name that is not there -
    /// the on-disk fingerprint of an Xbox sync that never finished.</summary>
    private static void RenameBlob(string root, Guid newName)
    {
        var folder = ContainerFolder(root);
        var blob = BlobFiles(folder).Single();
        File.Move(blob, Path.Combine(folder, newName.ToString("N").ToUpperInvariant()));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("abiotic-gp-safety-");
        public string Path => System.IO.Path.Combine(_dir.FullName, "wgs");
        public void Dispose()
        {
            try { _dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
