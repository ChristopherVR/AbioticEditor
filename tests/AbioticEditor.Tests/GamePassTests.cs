using System.Text;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Game Pass / Xbox container support. These build a synthetic wgs + ABF_SAVE_VERSION layout from
/// a real Steam fixture (no personal Game Pass data committed) and round-trip it. The Oodle-backed
/// bundle tests skip gracefully when no native Oodle library is available (e.g. offline CI).
/// </summary>
public class GamePassTests
{
    private const string CharClass = GamePassMemberCodec.CharacterSaveClass;

    private static byte[] FixturePlayer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void MemberCodec_strips_and_restores_header_losslessly()
    {
        var save = FixturePlayer();

        // A full Steam save is GVAS header + the same body a Game Pass member stores.
        var body = GamePassMemberCodec.ToMemberBody(CharClass, save);
        var rebuilt = GamePassMemberCodec.ToGvas(CharClass, body);

        // The body splits off cleanly: reconstruct -> parse -> re-serialize -> body is unchanged.
        var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(rebuilt)));
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        var reBody = GamePassMemberCodec.ToMemberBody(CharClass, ms.ToArray());
        Assert.Equal(body, reBody);
    }

    [Fact]
    public void WgsContainerStore_writes_and_reads_a_blob()
    {
        var dir = Directory.CreateTempSubdirectory("wgs-test");
        try
        {
            BuildSyntheticWgs(dir.FullName, "ForScience-WC", new byte[] { 1, 2, 3, 4, 5 });
            var store = WgsContainerStore.Open(dir.FullName);
            var c = store.Find("ForScience-WC");
            Assert.NotNull(c);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, store.ReadBlob(c!));

            // Write a new, larger blob; a fresh store must read it back and bump the generation.
            var oldNum = c!.ContainerNumber;
            store.WriteBlob(c, new byte[] { 9, 9, 9, 9, 9, 9, 9 });
            var reopened = WgsContainerStore.Open(dir.FullName);
            var c2 = reopened.Find("ForScience-WC")!;
            Assert.Equal(7, c2.BlobSize);
            Assert.Equal(unchecked((byte)(oldNum + 1)), c2.ContainerNumber);
            Assert.Equal(new byte[] { 9, 9, 9, 9, 9, 9, 9 }, reopened.ReadBlob(c2));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveContainerFolder_finds_the_index_from_any_nearby_level()
    {
        var parent = Directory.CreateTempSubdirectory("wgs-resolve");
        try
        {
            // The container folder (holds containers.index) sits one level under the picked parent,
            // mirroring a real "<...>\wgs\<account>" tree where <account> holds the index.
            var account = Path.Combine(parent.FullName, "00090000_ABCDEF");
            Directory.CreateDirectory(account);
            BuildSyntheticWgs(account, "W-WC", new byte[] { 1, 2, 3 });
            var blobSubfolder = Directory.EnumerateDirectories(account)
                .First(d => WgsContainerStore.IsContainerFolder(d) == false);

            // Picked the container folder itself.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(account));
            // Picked the parent ("wgs"): a child is the container folder.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(parent.FullName));
            // Picked a GUID blob sub-folder: its parent is the container folder.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(blobSubfolder));
            // An unrelated folder resolves to nothing.
            var unrelated = Path.Combine(parent.FullName, "nope");
            Directory.CreateDirectory(unrelated);
            Assert.Null(WgsContainerStore.ResolveContainerFolder(unrelated));
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public void AbfBundle_round_trips_through_oodle()
    {
        if (!OodleCodec.IsAvailable) return; // no native Oodle here - skip

        var body = GamePassMemberCodec.ToMemberBody(CharClass, FixturePlayer());
        var bundle = TestBundle(("Profile/Worlds/W/PlayerData/Player_1", CharClass, body));

        var blob = bundle.Serialize();
        Assert.True(AbfSaveBundle.LooksLikeBundle(blob));
        var reparsed = AbfSaveBundle.Parse(blob);

        Assert.Single(reparsed.Members);
        Assert.Equal(body, reparsed.Members[0].Body);
        Assert.Equal(CharClass, reparsed.Members[0].SaveClass);
    }

    /// <summary>
    /// Validates that OodleCodec.Compress produces a single-quantum stream for data larger than
    /// 512 KB. The default Oodle seekChunkLen (512 KB) would split > 512 KB inputs into multiple
    /// independent streams; OodleLZ_Decompress then only decodes the first quantum and returns
    /// 524288 instead of the full length, causing "OodleLZ_Decompress failed (524288 != N)".
    /// This test reproduces the exact failure the game reports.
    /// </summary>
    [Fact]
    public void OodleCompress_large_payload_roundtrips_as_single_quantum()
    {
        if (!OodleCodec.IsAvailable) return;

        // Use the same size as the real failing case (sum of world member bodies = 660738).
        const int Size = 660738;
        var original = new byte[Size];
        for (var i = 0; i < Size; i++) original[i] = (byte)(i * 7 % 251);

        var compressed = OodleCodec.Compress(original);

        // OodleCodec.Decompress must reconstruct the full Size bytes in one call - exactly
        // what the game's GDK reader does: OodleLZ_Decompress(blob, compSize, buf, totalSize).
        // If the compressed output has multiple quanta, Decompress would return only 524288
        // bytes (the first quantum) and throw "produced 524288 bytes, expected 660738".
        var decompressed = OodleCodec.Decompress(compressed, Size);
        Assert.Equal(Size, decompressed.Length);
        Assert.Equal(original, decompressed);
    }

    /// <summary>
    /// Same single-quantum contract for data exactly at the 512 KB boundary and just above it,
    /// since 512 KB (524288 bytes) is the default Oodle seek chunk limit that triggers splitting.
    /// </summary>
    [Theory]
    [InlineData(524287)] // just below 512 KB - always one quantum with default seek chunk
    [InlineData(524288)] // exactly 512 KB - boundary case
    [InlineData(524289)] // just above 512 KB - triggers splitting with default, not with our fix
    [InlineData(700000)] // well above 512 KB
    public void OodleCompress_roundtrips_sizes_around_512KB_boundary(int size)
    {
        if (!OodleCodec.IsAvailable) return;

        var original = new byte[size];
        for (var i = 0; i < size; i++) original[i] = (byte)(i * 13 % 251);

        var compressed = OodleCodec.Compress(original);
        var decompressed = OodleCodec.Decompress(compressed, size);

        Assert.Equal(size, decompressed.Length);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public void GamePassSaveSet_edits_a_packed_player_end_to_end()
    {
        if (!OodleCodec.IsAvailable) return;

        var dir = Directory.CreateTempSubdirectory("gp-set");
        try
        {
            // Pack a real player into a synthetic Game Pass world container.
            var body = GamePassMemberCodec.ToMemberBody(CharClass, FixturePlayer());
            var bundle = TestBundle(("Profile/Worlds/W/PlayerData/Player_2533274900397709", CharClass, body));
            BuildSyntheticWgs(dir.FullName, "W-WC", bundle.Serialize());

            var set = GamePassSaveSet.Open(dir.FullName);
            var entry = set.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            Assert.Equal("Player_2533274900397709.sav", entry.FileName);

            // Read -> edit money via the real reader/writer -> write back.
            var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(entry))));
            PlayerSaveWriter.ApplyStats(data, data.Stats with { Money = 123456 });
            using var ms = new MemoryStream();
            data.Raw.WriteTo(ms);
            set.WriteSave(entry, ms.ToArray());

            // Reopen the container from disk; the edit must be there and everything else intact.
            var reopened = GamePassSaveSet.Open(dir.FullName);
            var entry2 = reopened.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            var data2 = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(reopened.ReadSave(entry2))));
            Assert.Equal(123456, data2.Stats.Money);
            Assert.True(Directory.Exists(dir.FullName + ".bak"), "the wgs folder should be backed up on write");
        }
        finally
        {
            dir.Delete(recursive: true);
            if (Directory.Exists(dir.FullName + ".bak")) Directory.Delete(dir.FullName + ".bak", recursive: true);
        }
    }

    [Fact]
    public void RealFixture_lists_reads_and_edits_a_packed_player()
    {
        if (Fixtures.GamePassWgsDir is null) return; // fixture absent - skip
        if (!OodleCodec.IsAvailable) return;         // no native Oodle - skip

        // Work on a throwaway copy so the committed fixture is never mutated.
        var work = Directory.CreateTempSubdirectory("gp-fixture");
        try
        {
            CopyTree(Fixtures.GamePassWgsDir!, work.FullName);

            var set = GamePassSaveSet.Open(work.FullName);
            var entries = set.Entries();
            Assert.Contains(entries, e => e.Kind == GamePassSaveKind.Player);
            Assert.Contains(entries, e => e.Kind == GamePassSaveKind.WorldMetadata);

            var player = entries.First(e => e.Kind == GamePassSaveKind.Player);

            // The real Game Pass member reconstructs into a save the editor parses.
            var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(player))));
            Assert.NotEmpty(data.Skills);

            // Edit -> write back -> reopen from disk -> the edit survives the wgs/ABF/Oodle round-trip.
            PlayerSaveWriter.ApplyStats(data, data.Stats with { Money = 314159 });
            using var ms = new MemoryStream();
            data.Raw.WriteTo(ms);
            set.WriteSave(player, ms.ToArray());

            var reopened = GamePassSaveSet.Open(work.FullName);
            var p2 = reopened.Entries().First(e => e.Kind == GamePassSaveKind.Player);
            var data2 = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(reopened.ReadSave(p2))));
            Assert.Equal(314159, data2.Stats.Money);
        }
        finally
        {
            work.Delete(recursive: true);
            if (Directory.Exists(work.FullName + ".bak")) Directory.Delete(work.FullName + ".bak", recursive: true);
        }
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }

    [Fact]
    public void Converts_Steam_world_to_GamePass_and_back_losslessly()
    {
        if (Fixtures.CascadeDir is null) return; // fixture absent - skip
        if (!OodleCodec.IsAvailable) return;     // no native Oodle - skip

        var tmp = Directory.CreateTempSubdirectory("steam-gp-convert");
        try
        {
            // A minimal Steam world: the metadata + one player (copied from the Cascade fixture).
            var steam = Path.Combine(tmp.FullName, "MyWorld");
            Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
                Path.Combine(steam, "WorldSave_MetaData.sav"));
            var srcPlayer = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
            var playerName = Path.GetFileName(srcPlayer);
            File.Copy(srcPlayer, Path.Combine(steam, "PlayerData", playerName));

            // Steam -> Game Pass.
            var wgs = GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(tmp.FullName, "gp"));
            Assert.True(GamePassSaveSet.IsGamePassFolder(wgs));
            var set = GamePassSaveSet.Open(wgs);
            Assert.Contains(set.Entries(), e => e.FileName == playerName);

            // Game Pass -> Steam, into a new folder.
            var back = GamePassConverter.GamePassToSteamWorld(wgs, $"MyWorld-WC", Path.Combine(tmp.FullName, "back"));

            // The player save survives the round-trip byte-for-byte.
            Assert.True(File.Exists(Path.Combine(back, "PlayerData", playerName)));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(steam, "PlayerData", playerName)),
                File.ReadAllBytes(Path.Combine(back, "PlayerData", playerName)));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(steam, "WorldSave_MetaData.sav")),
                File.ReadAllBytes(Path.Combine(back, "WorldSave_MetaData.sav")));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Conversion_can_rehome_the_player_to_a_new_id()
    {
        if (Fixtures.CascadeDir is null) return;
        if (!OodleCodec.IsAvailable) return;

        var tmp = Directory.CreateTempSubdirectory("gp-rehome");
        try
        {
            var steam = Path.Combine(tmp.FullName, "W");
            Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
                Path.Combine(steam, "WorldSave_MetaData.sav"));
            var srcPlayer = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
            File.Copy(srcPlayer, Path.Combine(steam, "PlayerData", Path.GetFileName(srcPlayer)));

            const string newId = "msft-9Z8Y7X";
            var wgs = GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(tmp.FullName, "gp"), worldName: "W", newPlayerId: newId);

            var set = GamePassSaveSet.Open(wgs);
            var player = set.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            Assert.Equal($"Player_{newId}.sav", player.FileName);
            // The SaveIdentifier inside the save was re-homed too.
            Assert.Equal(newId, AbioticEditor.Core.PlayerSaves.PlayerSaveIdentity.GetSaveIdentifier(
                UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(player)))));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // ---- helpers: build a minimal but real wgs container folder + ABF bundle ----

    private static AbfSaveBundle TestBundle(params (string Path, string Class, byte[] Body)[] members)
    {
        // Re-create via Parse(Serialize(...)) is circular, so build the blob by hand-serializing a
        // bundle we construct through its own Serialize. We do that by faking a parse from a
        // minimal hand-built blob: simplest is to use reflection-free construction via Serialize of
        // a bundle assembled from a round-tripped empty. Instead, assemble the blob bytes directly.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        WriteStr(w, "ABF_SAVE_VERSION");
        w.Write(3);                       // version
        w.Write(0);                       // field1
        w.Write(16);                      // field2
        w.Write(members.Length);
        foreach (var m in members)
        {
            WriteStr(w, m.Path);
            w.Write(m.Body.Length);
            WriteStr(w, m.Class);
            w.Write(0);                   // flag
        }
        var raw = members.SelectMany(m => m.Body).ToArray();
        var comp = OodleCodec.Compress(raw);
        w.Write(1);                       // method = Oodle
        w.Write(comp.Length);
        w.Flush();
        ms.Write(comp, 0, comp.Length);
        return AbfSaveBundle.Parse(ms.ToArray());
    }

    private static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        w.Write(b.Length + 1);
        w.Write(b);
        w.Write((byte)0);
    }

    private static void BuildSyntheticWgs(string root, string containerName, byte[] blob)
    {
        var folderGuid = Guid.NewGuid();
        var folderName = folderGuid.ToString("N").ToUpperInvariant();
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);

        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);

        // container.1 manifest
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(4u); w.Write(1u);
            var nameField = new byte[128];
            Encoding.Unicode.GetBytes("Data").CopyTo(nameField, 0);
            w.Write(nameField);
            w.Write(blobGuid.ToByteArray());
            w.Write(blobGuid.ToByteArray());
            File.WriteAllBytes(Path.Combine(folder, "container.1"), ms.ToArray());
        }

        // containers.index
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, Encoding.Unicode))
        {
            w.Write(14u);                 // version
            w.Write(1u);                  // container count
            w.Write(0u);                  // reserved
            WriteWStr(w, "Synthetic.Abiotic_Test!App");
            w.Write(DateTime.UtcNow.ToFileTimeUtc());
            w.Write(3u);
            WriteWStr(w, Guid.NewGuid().ToString());
            w.Write(new byte[8]);         // 8 reserved bytes
            WriteWStr(w, containerName);
            WriteWStr(w, containerName);
            WriteWStr(w, "\"0x1\"");
            w.Write((byte)1);             // container number -> container.1
            w.Write(1u);                  // generation
            w.Write(folderGuid.ToByteArray());
            w.Write(DateTime.UtcNow.ToFileTimeUtc());
            w.Write(0L);
            w.Write((long)blob.Length);
            File.WriteAllBytes(Path.Combine(root, "containers.index"), ms.ToArray());
        }
    }

    private static void WriteWStr(BinaryWriter w, string s)
    {
        w.Write((uint)s.Length);
        w.Write(Encoding.Unicode.GetBytes(s));
    }
}
